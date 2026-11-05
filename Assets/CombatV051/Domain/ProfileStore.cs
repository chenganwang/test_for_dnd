using System;
using System.IO;
using System.Text;

namespace CombatV051.Domain
{
    [Serializable] public sealed class ProfileEnvelope
    {
        public int schemaVersion=5;
        public string contentVersion="actioncrpg-v0.5.2-p4",integrityAlgorithm="sha256-utf8-payload-v1",profileId,profileInstanceId,payload,integrity;
        public long revision;
    }
    // Same durable replacement/rollback protocol as CombatStore. P2 has one outer file,
    // an independent envelope, and never writes a second inner combat save.
    public sealed class ProfileStore
    {
        public string MainPath {get;private set;}
        public string BackupPath {get{return MainPath+".bak";}}
        public string TemporaryPath {get{return MainPath+".tmp";}}
        public bool UsedBackup {get;private set;}
        public Action<CombatWriteStage> WriteFault;
        readonly Action<ProfileState> validate;
        bool hasAuthority;long authorityRevision;string authorityDigest,authorityEncoded,authorityProfileId,authorityInstanceId;
        byte[] readbackBuffer;
        public ProfileStore(string path,Action<ProfileState> validate){MainPath=Path.GetFullPath(path);this.validate=validate;}
        public ProfileState Load()
        {
            using(var ownership=AcquireOwnership())return LoadCore();
        }
        ProfileState LoadCore()
        {
            string main=ReadRaw(MainPath);RejectWrongVersion(main);ProfileState state;string mainError,backupError;
            if(TryRead(main,out state,out mainError)){UsedBackup=false;Adopt(state,main);return state;}
            string backup=ReadRaw(BackupPath);
            if(TryRead(backup,out state,out backupError)){CheckBackupIdentity(state,main);UsedBackup=true;Adopt(state,backup);return state;}
            throw new IOException("No valid P4 Profile. Main: "+mainError+"; backup: "+backupError+". Existing files retained.");
        }
        public void Save(ProfileState candidate)
        {
            using(var ownership=AcquireOwnership())SaveCore(candidate);
        }
        void SaveCore(ProfileState candidate)
        {
            validate(candidate);string old=null,error;bool replaced=false,created=false,mainValid=false;ProfileState known;
            using(DomainTiming.Measure("Store.ReadCAS"))
            {
                // Compare actual bytes under the ownership lock. Only an exact image previously
                // fully validated by Load or durably written by this instance can take the fast path.
                string main=ReadRaw(MainPath);
                if(hasAuthority&&main!=null&&string.Equals(main,authorityEncoded,StringComparison.Ordinal))
                {CheckCachedAuthority(candidate);mainValid=true;old=main;}
                else
                {
                    RejectWrongVersion(main);
                    if(TryRead(main,out known,out error)){CheckAuthority(known,candidate);mainValid=true;old=main;}
                    else
                    {
                        string backup=ReadRaw(BackupPath);
                        if(TryRead(backup,out known,out error)){CheckAuthority(known,candidate);old=backup;}
                        else if(File.Exists(MainPath)||File.Exists(BackupPath))throw new IOException("Existing profile is invalid; refusing overwrite or implicit migration.");
                    }
                }
            }
            try
            {
                string payload,payloadDigest,encoded;
                using(DomainTiming.Measure("Store.Encode"))
                {
                    // Candidate validation is complete. Encoding is deliberately not a load:
                    // migration callbacks must never rewrite live state while copying/saving.
                    payload=CombatJson.Write(candidate);payloadDigest=CombatStore.Digest(payload);
                    var e=new ProfileEnvelope{profileId=candidate.profileId,profileInstanceId=candidate.profileInstanceId,revision=candidate.revision,payload=payload,integrity=payloadDigest};
                    encoded=CombatJson.Write(e);
                }
                byte[] encodedBytes;
                using(DomainTiming.Measure("Store.UTF8Encode"))encodedBytes=new UTF8Encoding(false).GetBytes(encoded);
                DomainTiming.RecordProfileSave(candidate,payload,encodedBytes.Length);
                using(DomainTiming.Measure("Store.WriteFlush"))
                {Fault(CombatWriteStage.BeforeWrite);WriteDurable(TemporaryPath,encodedBytes);Fault(CombatWriteStage.AfterFlush);}
                using(DomainTiming.Measure("Store.ReplaceVerify"))
                {
                    using(DomainTiming.Measure("Store.TempReadback"))
                        if(!MatchesBytes(TemporaryPath,encodedBytes))throw new IOException("temporary_validation_failed:bytes_changed");
                    Fault(CombatWriteStage.BeforeReplace);
                    using(DomainTiming.Measure("Store.AtomicReplace"))
                    {if(File.Exists(MainPath))File.Replace(TemporaryPath,MainPath,mainValid?BackupPath:null);else{File.Move(TemporaryPath,MainPath);created=true;}}
                    replaced=true;Fault(CombatWriteStage.AfterReplace);
                    using(DomainTiming.Measure("Store.MainReadback"))
                        if(!MatchesBytes(MainPath,encodedBytes))throw new IOException("saved_validation_failed:bytes_changed");
                    Fault(CombatWriteStage.AfterVerify);
                }
                Adopt(candidate,encoded,payloadDigest);
            }
            catch
            {
                if(replaced){if(old!=null){WriteDurable(TemporaryPath,old);File.Replace(TemporaryPath,MainPath,null);}else if(created)File.Move(MainPath,MainPath+".uncommitted-"+Guid.NewGuid().ToString("N"));}
                throw;
            }
        }
        FileStream AcquireOwnership()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MainPath));
            // This persistent per-path file protects the whole authority/read/replace/rollback
            // interval across processes. Never delete it: deletion permits a second lock inode.
            // FileShare.None fails immediately for another owner; there is no wait or reentry.
            return new FileStream(MainPath+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        }
        void CheckAuthority(ProfileState known,ProfileState candidate)
        {
            if(!hasAuthority)throw new IOException("load_existing_profile_first");
            if(known.profileId!=candidate.profileId||known.profileInstanceId!=candidate.profileInstanceId)throw new IOException("profile_identity_mismatch");
            if(known.revision!=authorityRevision||CombatStore.Digest(CombatJson.Write(known))!=authorityDigest||known.revision>candidate.revision)throw new IOException("stale_profile_authority");
        }
        void CheckCachedAuthority(ProfileState candidate)
        {
            if(authorityProfileId!=candidate.profileId||authorityInstanceId!=candidate.profileInstanceId)throw new IOException("profile_identity_mismatch");
            if(authorityRevision>candidate.revision)throw new IOException("stale_profile_authority");
        }
        void Adopt(ProfileState state,string encoded,string digest=null)
        {hasAuthority=true;authorityRevision=state.revision;authorityDigest=digest??CombatStore.Digest(CombatJson.Write(state));authorityProfileId=state.profileId;authorityInstanceId=state.profileInstanceId;authorityEncoded=encoded;}
        void CheckBackupIdentity(ProfileState backup,string main)
        {
            if(main==null)return;
            Newtonsoft.Json.Linq.JObject header;try{header=Newtonsoft.Json.Linq.JObject.Parse(main);}catch{return;}
            // An intact identity header still constrains recovery even if its payload was corrupted.
            string id=(string)header["profileId"],instance=(string)header["profileInstanceId"];
            if(!string.IsNullOrEmpty(id)&&id!=backup.profileId||!string.IsNullOrEmpty(instance)&&instance!=backup.profileInstanceId)throw new IOException("backup_profile_identity_mismatch");
        }
        static void RejectWrongVersion(string encoded)
        {
            if(encoded==null)return;
            // Inspect version before strict typed deserialization: old bare Combat envelopes
            // have fields unknown to ProfileEnvelope and must not fall through to a P2 backup.
            Newtonsoft.Json.Linq.JObject e;try{e=Newtonsoft.Json.Linq.JObject.Parse(encoded);}catch{return;}
            if((int?)e["schemaVersion"]!=5||(string)e["contentVersion"]!="actioncrpg-v0.5.2-p4")throw new IOException("unsupported_profile_version");
        }
        static string ReadRaw(string path)
        {try{return File.Exists(path)?File.ReadAllText(path,Encoding.UTF8):null;}catch{return null;}}
        bool TryRead(string encoded,out ProfileState state,out string error)
        {
            state=null;try
            {
                if(encoded==null)throw new FileNotFoundException("absent_or_unreadable");var e=CombatJson.Read<ProfileEnvelope>(encoded);
                if(e==null||e.schemaVersion!=5||e.contentVersion!="actioncrpg-v0.5.2-p4"||e.integrityAlgorithm!="sha256-utf8-payload-v1")throw new IOException("unsupported_profile_version");
                if(e.payload==null||e.integrity!=CombatStore.Digest(e.payload))throw new IOException("integrity_failed");state=CombatJson.Read<ProfileState>(e.payload);validate(state);
                if(state.profileId!=e.profileId||state.profileInstanceId!=e.profileInstanceId||state.revision!=e.revision)throw new IOException("envelope_identity_mismatch");error=null;return true;
            }
            catch(Exception ex){state=null;error=ex.Message;return false;}
        }
        // Both verification reads compare the exact bytes submitted to the durable write.
        // The bounded scratch buffer is reused by this store, which already has exclusive
        // ownership of the whole transaction. No decoding, hashing, or size-only shortcut.
        bool MatchesBytes(string path,byte[] expected)
        {
            if(readbackBuffer==null)readbackBuffer=new byte[16*1024];
            using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,1))
            {
                if(f.Length!=expected.Length)return false;
                int offset=0;
                while(offset<expected.Length)
                {
                    int count=f.Read(readbackBuffer,0,Math.Min(readbackBuffer.Length,expected.Length-offset));
                    if(count==0)return false;
                    for(int i=0;i<count;i++)if(readbackBuffer[i]!=expected[offset+i])return false;
                    offset+=count;
                }
                return f.ReadByte()==-1;
            }
        }
        static void WriteDurable(string path,string value){WriteDurable(path,new UTF8Encoding(false).GetBytes(value));}
        static void WriteDurable(string path,byte[] bytes)
        {
            FileStream f=null;
            try
            {
                using(DomainTiming.Measure("Store.TempWrite"))
                {f=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None);f.Write(bytes,0,bytes.Length);}
                using(DomainTiming.Measure("Store.DurableFlush"))f.Flush(true);
            }
            finally{if(f!=null)f.Dispose();}
        }
        void Fault(CombatWriteStage stage){if(WriteFault!=null)WriteFault(stage);}
    }
}
