# 存档兼容

默认档案位于 Unity 的 `Application.persistentDataPath` 下，子目录为 `CombatV051/V14-Demo/profile-1/`。新局在出发时冻结模式、技能和核心卡。旧 P4/V12/V13 局按原快照继续。未知标识或完整性检查失败时应保留原件和备份。

如需打开旧档，先复制整个档案目录，在解压后的 Windows 游戏目录执行以下命令。将路径换为自己的副本。

```powershell
$profile = (Resolve-Path .\saved-copy\profile.json).Path
.\ActionCRPG.exe -p3-playtest-profile $profile
```

进入“探索 → 继续”。此参数只指定档案位置。
