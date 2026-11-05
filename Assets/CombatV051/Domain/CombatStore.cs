using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CombatV051.Domain
{
    public enum CombatWriteStage { BeforeWrite,AfterFlush,BeforeReplace,AfterReplace,AfterVerify }
    [Serializable] public sealed class CombatEnvelope
    {
        public int schemaVersion=2;
        public string contentVersion="actioncrpg-v0.5.1-p0",integrityAlgorithm="sha256-utf8-payload-v1",profileId,modeInstanceId,payload,integrity;
        public long revision;
    }
    public sealed class CombatStore
    {
        public string MainPath {get;private set;}
        public string BackupPath {get{return MainPath+".bak";}}
        public string TemporaryPath {get{return MainPath+".tmp";}}
        public bool UsedBackup {get;private set;}
        public Action<CombatWriteStage> WriteFault;
        bool hasAuthority;
        long authorityRevision;
        string authorityDigest;
        public string ExpectedRulesProfileId {get;private set;}
        public CombatStore(string path,string expectedRulesProfileId="p0-single"){MainPath=Path.GetFullPath(path);if(expectedRulesProfileId!="p0-single"&&expectedRulesProfileId!="p1-party")throw new InvalidDataException("unsupported_rules_profile");ExpectedRulesProfileId=expectedRulesProfileId;}
        public CombatState Load()
        {
            RejectWrongProfileFile(MainPath);
            CombatState result;string mainError,backupError;
            if(TryRead(MainPath,out result,out mainError)){UsedBackup=false;Adopt(result);return result;}
            if(TryRead(BackupPath,out result,out backupError)){UsedBackup=true;Adopt(result);return result;}
            throw new IOException("No valid v051 snapshot. Main: "+mainError+"; backup: "+backupError+". Existing files retained.");
        }
        public void Save(CombatState state)
        {
            CombatSession.Validate(state);
            if(state.rulesProfileId!=ExpectedRulesProfileId)throw new InvalidDataException("save_rules_profile_mismatch");
            RejectWrongProfileFile(MainPath);
            string old=null;bool replaced=false,created=false,mainValid=false;
            CombatState known;string error;
            if(TryRead(MainPath,out known,out error))
            {
                CheckAuthority(known);mainValid=true;
                if(known.profileId!=state.profileId||known.modeInstanceId!=state.modeInstanceId)throw new InvalidDataException("save_identity_mismatch");
                if(known.revision>state.revision)throw new InvalidDataException("stale_save_revision");
                old=File.ReadAllText(MainPath,Encoding.UTF8);
            }
            else if(TryRead(BackupPath,out known,out error))
            {
                CheckAuthority(known);
                if(known.profileId!=state.profileId||known.modeInstanceId!=state.modeInstanceId)throw new InvalidDataException("save_identity_mismatch");
                old=File.ReadAllText(BackupPath,Encoding.UTF8);
            }
            else if(File.Exists(MainPath)||File.Exists(BackupPath))throw new InvalidDataException("Existing save is not a valid v051 snapshot; refusing overwrite or implicit legacy migration.");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(MainPath));
                string payload=CombatJson.Write(state);
                var envelope=new CombatEnvelope{schemaVersion=state.schemaVersion,contentVersion=state.contentVersion,profileId=state.profileId,modeInstanceId=state.modeInstanceId,revision=state.revision,payload=payload,integrity=Digest(payload)};
                Fault(CombatWriteStage.BeforeWrite);WriteDurable(TemporaryPath,CombatJson.Write(envelope));Fault(CombatWriteStage.AfterFlush);
                CombatState temp;if(!TryRead(TemporaryPath,out temp,out error))throw new IOException("temporary_validation_failed:"+error);
                Fault(CombatWriteStage.BeforeReplace);
                if(File.Exists(MainPath))File.Replace(TemporaryPath,MainPath,mainValid?BackupPath:null);
                else{File.Move(TemporaryPath,MainPath);created=true;}
                replaced=true;Fault(CombatWriteStage.AfterReplace);
                CombatState verified;if(!TryRead(MainPath,out verified,out error)||verified.revision!=state.revision)throw new IOException("saved_validation_failed:"+error);
                Fault(CombatWriteStage.AfterVerify);
                Adopt(state);
            }
            catch
            {
                if(replaced)
                {
                    if(old!=null){WriteDurable(TemporaryPath,old);File.Replace(TemporaryPath,MainPath,null);}
                    else if(created){string failed=MainPath+".uncommitted-"+Guid.NewGuid().ToString("N");File.Move(MainPath,failed);}
                }
                throw;
            }
        }
        void CheckAuthority(CombatState known)
        {
            if(!hasAuthority)throw new InvalidDataException("load_existing_save_first");
            if(known.revision!=authorityRevision||Digest(CombatJson.Write(known))!=authorityDigest)throw new InvalidDataException("stale_save_authority");
        }
        void Adopt(CombatState state){hasAuthority=true;authorityRevision=state.revision;authorityDigest=Digest(CombatJson.Write(state));}
        void RejectWrongProfileFile(string path)
        {
            if(!File.Exists(path))return;CombatEnvelope e;
            try{e=CombatJson.Read<CombatEnvelope>(File.ReadAllText(path,Encoding.UTF8));}catch{return;}
            if(e!=null&&!VersionMatches(e))throw new IOException("unsupported_save_version_or_profile");
        }
        bool VersionMatches(CombatEnvelope e){return ExpectedRulesProfileId=="p1-party"?e.schemaVersion==3&&e.contentVersion=="actioncrpg-v0.5.1-p1":e.schemaVersion==2&&e.contentVersion=="actioncrpg-v0.5.1-p0";}
        bool TryRead(string path,out CombatState state,out string error)
        {
            state=null;
            try
            {
                if(!File.Exists(path))throw new FileNotFoundException("absent");
                var e=CombatJson.Read<CombatEnvelope>(File.ReadAllText(path,Encoding.UTF8));
                if(e==null||!VersionMatches(e)||e.integrityAlgorithm!="sha256-utf8-payload-v1")throw new InvalidDataException("unsupported_save_version");
                if(e.payload==null||e.integrity!=Digest(e.payload))throw new InvalidDataException("integrity_failed");
                state=CombatJson.Read<CombatState>(e.payload);CombatSession.Validate(state);
                if(state.profileId!=e.profileId||state.modeInstanceId!=e.modeInstanceId||state.revision!=e.revision||state.schemaVersion!=e.schemaVersion||state.contentVersion!=e.contentVersion||state.rulesProfileId!=ExpectedRulesProfileId)throw new InvalidDataException("envelope_identity_mismatch");
                error=null;return true;
            }
            catch(Exception ex){state=null;error=ex.Message;return false;}
        }
        public static string Digest(string value){using(var sha=SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));}
        static void WriteDurable(string path,string value){byte[] data=new UTF8Encoding(false).GetBytes(value);using(var f=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None)){f.Write(data,0,data.Length);f.Flush(true);}}
        void Fault(CombatWriteStage stage){if(WriteFault!=null)WriteFault(stage);}
    }
}
