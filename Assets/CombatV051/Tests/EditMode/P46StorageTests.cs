using System;
using System.IO;
using System.Text;
using CombatV051.Domain;
using NUnit.Framework;

namespace CombatV051.Tests
{
    public sealed class P46StorageTests:ProfileTestFixture
    {
        string directory,path;

        [SetUp] public void CreateIsolatedStore()
        {
            directory=Path.Combine(Path.GetTempPath(),"CombatV052StorageRules",Guid.NewGuid().ToString("N"));
            path=Path.Combine(directory,"P4","profile.json");
            profile=new ProfileSession(profile.State,profile.BaseCombatConfig,profile.Config,path);
            profile.Store.Save(profile.State);
        }

        [TearDown] public void RemoveIsolatedStore()
        {
            if(Directory.Exists(directory))Directory.Delete(directory,true);
        }

        [TestCase(CombatWriteStage.AfterFlush,"temporary_validation_failed:bytes_changed")]
        [TestCase(CombatWriteStage.AfterReplace,"saved_validation_failed:bytes_changed")]
        public void ValidButChangedWrittenEnvelopeIsRejectedAndOriginalPaidAuthorityIsRestored(CombatWriteStage stage,string expectedError)
        {
            Travel("R1");Activate();Core(CommandKind.EnterFocus);
            byte[] originalMain=File.ReadAllBytes(path),originalBackup=File.ReadAllBytes(profile.Store.BackupPath);
            string originalProfile=CombatJson.Write(profile.State),originalCombat=CombatJson.Write(CState);
            int faultVisits=0;
            profile.Store.WriteFault=point=>
            {
                if(point!=stage)return;
                faultVisits++;
                string target=stage==CombatWriteStage.AfterFlush?profile.Store.TemporaryPath:path;
                var envelope=CombatJson.Read<ProfileEnvelope>(File.ReadAllText(target,Encoding.UTF8));
                var altered=CombatJson.Read<ProfileState>(envelope.payload);
                altered.meta.research+=17;
                profile.Validate(altered); // A valid alternate state must still fail exact-write verification.
                envelope.payload=CombatJson.Write(altered);
                envelope.integrity=CombatStore.Digest(envelope.payload);
                File.WriteAllText(target,CombatJson.Write(envelope),new UTF8Encoding(false));
            };
            var command=new CombatCommand{id="p46-exact-confirm",kind=CommandKind.ConfirmRoll,actorId="fighter",targetId=CState.enemies[0].id,actionId="attack"};
            var result=profile.ExecuteCombat(command);

            Assert.That(faultVisits,Is.EqualTo(1));
            Assert.That(result.success,Is.False);
            Assert.That(result.reason,Is.EqualTo(expectedError));
            Assert.That(CombatJson.Write(profile.State),Is.EqualTo(originalProfile));
            Assert.That(CombatJson.Write(CState),Is.EqualTo(originalCombat),"No paid face, random draw or Charge may be published on failure.");
            Assert.That(File.ReadAllBytes(path),Is.EqualTo(originalMain));
            Assert.That(File.ReadAllBytes(profile.Store.BackupPath),Is.EqualTo(stage==CombatWriteStage.AfterReplace?originalMain:originalBackup));
            var restored=ProfileSession.Load(path,profile.BaseCombatConfig,profile.Config);
            Assert.That(CombatJson.Write(restored.State),Is.EqualTo(originalProfile));
            Assert.That(CombatJson.Write(restored.ActiveCombat.State),Is.EqualTo(originalCombat));

            // The failed attempt must not advance the original store's authority or consume its command receipt.
            profile.Store.WriteFault=null;
            Assert.That(profile.ExecuteCombat(command).success);
            string committedProfile=CombatJson.Write(profile.State),committedCombat=CombatJson.Write(CState);
            byte[] committedMain=File.ReadAllBytes(path);
            Assert.That(profile.ExecuteCombat(command).duplicate);
            Assert.That(CombatJson.Write(CState),Is.EqualTo(committedCombat));
            Assert.That(File.ReadAllBytes(path),Is.EqualTo(committedMain));
            restored=ProfileSession.Load(path,profile.BaseCombatConfig,profile.Config);
            Assert.That(CombatJson.Write(restored.State),Is.EqualTo(committedProfile));
            Assert.That(CombatJson.Write(restored.ActiveCombat.State),Is.EqualTo(committedCombat));
        }
    }
}
