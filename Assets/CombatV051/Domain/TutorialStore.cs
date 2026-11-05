using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace CombatV051.Domain
{
    [Serializable] public sealed class TutorialEnvelope
    {
        public int schemaVersion=1;
        public string kind="v051-tutorial-sidecar",profileInstanceId,storyModeId,payload,integrity;
        public long revision;
    }
    // This store is only for presentation progress. It must never receive a combat save path.
    public sealed class TutorialStore
    {
        public string MainPath {get;private set;}
        public string BackupPath {get{return MainPath+".bak";}}
        public string TemporaryPath {get{return MainPath+".tmp";}}
        public bool UsedBackup {get;private set;}
        public Action<CombatWriteStage> WriteFault;
        readonly Action<TutorialState> validate;
        bool hasAuthority;long authorityRevision;string authorityDigest;
        public TutorialStore(string path,Action<TutorialState> validate){MainPath=Path.GetFullPath(path);this.validate=validate??throw new ArgumentNullException(nameof(validate));}
        public TutorialState Load()
        {
            using(var ownership=Lock())
            {
                CheckKind(MainPath);TutorialState state;string mainError,backupError;
                if(TryRead(MainPath,out state,out mainError)){UsedBackup=false;Adopt(state);return state;}
                if(TryRead(BackupPath,out state,out backupError)){CheckReadableIdentity(MainPath,state);UsedBackup=true;Adopt(state);return state;}
                throw new IOException("tutorial_unavailable:"+mainError+"; "+backupError);
            }
        }
        public void Save(TutorialState candidate)
        {
            using(var ownership=Lock())
            {
                validate(candidate);CheckKind(MainPath);CheckKind(BackupPath);TutorialState known;string error;
                bool mainValid=TryRead(MainPath,out known,out error),knownValid=mainValid;
                if(!knownValid)knownValid=TryRead(BackupPath,out known,out error);
                if(knownValid)
                {
                    if(!hasAuthority)throw new IOException("load_tutorial_first");
                    if(known.profileInstanceId!=candidate.profileInstanceId||known.storyModeId!=candidate.storyModeId)throw new IOException("tutorial_identity_mismatch");
                    if(known.revision!=authorityRevision||CombatStore.Digest(CombatJson.Write(known))!=authorityDigest||known.revision>candidate.revision)throw new IOException("stale_tutorial_authority");
                }
                else
                {
                    CheckReadableIdentity(MainPath,candidate);CheckReadableIdentity(BackupPath,candidate);
                    // Retain malformed presentation data before starting a safe replacement.
                    if(File.Exists(MainPath))File.Copy(MainPath,MainPath+".rejected-"+Guid.NewGuid().ToString("N"));
                }
                string old=File.Exists(MainPath)?File.ReadAllText(MainPath,Encoding.UTF8):null;bool replaced=false,created=false;
                try
                {
                    string payload=CombatJson.Write(candidate);var envelope=new TutorialEnvelope{profileInstanceId=candidate.profileInstanceId,storyModeId=candidate.storyModeId,revision=candidate.revision,payload=payload,integrity=CombatStore.Digest(payload)};
                    Fault(CombatWriteStage.BeforeWrite);Durable(TemporaryPath,CombatJson.Write(envelope));Fault(CombatWriteStage.AfterFlush);
                    TutorialState temporary;if(!TryRead(TemporaryPath,out temporary,out error))throw new IOException("tutorial_temporary_invalid:"+error);
                    Fault(CombatWriteStage.BeforeReplace);if(File.Exists(MainPath))File.Replace(TemporaryPath,MainPath,mainValid?BackupPath:null);else{File.Move(TemporaryPath,MainPath);created=true;}replaced=true;
                    Fault(CombatWriteStage.AfterReplace);TutorialState verified;if(!TryRead(MainPath,out verified,out error)||verified.revision!=candidate.revision)throw new IOException("tutorial_verify_failed:"+error);
                    Fault(CombatWriteStage.AfterVerify);Adopt(candidate);
                }
                catch
                {
                    if(replaced){if(old!=null){Durable(TemporaryPath,old);File.Replace(TemporaryPath,MainPath,null);}else if(created)File.Move(MainPath,MainPath+".uncommitted-"+Guid.NewGuid().ToString("N"));}
                    throw;
                }
            }
        }
        FileStream Lock(){Directory.CreateDirectory(Path.GetDirectoryName(MainPath));return new FileStream(MainPath+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
        void Adopt(TutorialState state){hasAuthority=true;authorityRevision=state.revision;authorityDigest=CombatStore.Digest(CombatJson.Write(state));}
        static JObject Header(string path){if(!File.Exists(path))return null;try{return JObject.Parse(File.ReadAllText(path,Encoding.UTF8));}catch{return null;}}
        static void CheckKind(string path)
        {var header=Header(path);if(header!=null&&((string)header["kind"]!="v051-tutorial-sidecar"||(int?)header["schemaVersion"]!=1))throw new IOException("not_supported_tutorial_sidecar");}
        static void CheckReadableIdentity(string path,TutorialState state)
        {var h=Header(path);if(h==null)return;string profile=(string)h["profileInstanceId"],mode=(string)h["storyModeId"];if(!string.IsNullOrEmpty(profile)&&profile!=state.profileInstanceId||!string.IsNullOrEmpty(mode)&&mode!=state.storyModeId)throw new IOException("tutorial_identity_mismatch");}
        bool TryRead(string path,out TutorialState state,out string error)
        {
            state=null;
            try
            {
                if(!File.Exists(path))throw new FileNotFoundException("absent");var e=CombatJson.Read<TutorialEnvelope>(File.ReadAllText(path,Encoding.UTF8));
                if(e.schemaVersion!=1||e.kind!="v051-tutorial-sidecar"||e.payload==null||e.integrity!=CombatStore.Digest(e.payload))throw new IOException("invalid_tutorial_envelope");
                state=CombatJson.Read<TutorialState>(e.payload);validate(state);if(state.profileInstanceId!=e.profileInstanceId||state.storyModeId!=e.storyModeId||state.revision!=e.revision)throw new IOException("tutorial_envelope_identity_mismatch");error=null;return true;
            }
            catch(Exception ex){state=null;error=ex.Message;return false;}
        }
        static void Durable(string path,string contents){byte[] bytes=new UTF8Encoding(false).GetBytes(contents);using(var f=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None)){f.Write(bytes,0,bytes.Length);f.Flush(true);}}
        void Fault(CombatWriteStage stage){if(WriteFault!=null)WriteFault(stage);}
    }
}
