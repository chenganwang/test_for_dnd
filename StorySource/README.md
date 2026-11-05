# M2 作者文本入口

编辑本目录的 `story-text.json`，然后在 Unity 菜单执行 **Action Dice → Import Author Text (M2)**。

```json
{
  "title": "[标题待填写]",
  "introduction": "[剧情待填写]",
  "ending": "[剧情待填写]"
}
```

文件使用 UTF-8 编码。这三个字段必须存在，并且是非空字符串。未提供的内容请保留明确的“待填写”占位。可以用 JSON 的 `\n` 表达换行。导入器逐字保留内容，不补充角色、动机、线索、世界观或结局。

## 映射和保护

| 作者字段 | 游戏文本入口 |
| --- | --- |
| `title` | `StoryText.Load().title` |
| `introduction` | `StoryText.Load().introduction` |
| `ending` | `StoryText.Load().ending` |

导入器只读取**当前工程根目录**的 `StorySource/story-text.json`。它不写作者源，不遍历父目录、旧项目或其他故事。未知字段、重复字段、缺失字段和非字符串字段会被拒绝，不静默忽略。

输出位于 `Assets/Game/Resources/StoryText.json`，带生成器标记、固定来源路径和源文件 SHA-256。这里是生成资源；后续修改请回到作者源再导入。已有输出缺少正确生成器标记时，导入器拒绝覆盖；导入不删除或重建 `.meta`。

`Reports/author-import.json` 记录本次状态、逐字段映射、源文件导入前后哈希、输出哈希、占位情况和未接入的 chapter 字段。`AUTHOR_IMPORT_MAPPING` 证据不代表规则、引擎、构建或真人试玩通过。

兼容验证副本若没有 `StorySource/story-text.json`，菜单会明确记录 `SKIPPED_SOURCE_ABSENT_USING_IMPORTED_RESOURCE`，保留已经导入的资源；不会到副本外寻找作者稿件。源与已导入资源都缺失时则报错。

## 当前范围

**这是 M2 三个文本字段的占位导入，不是完整 chapter 节点图导入。** 四房间的固定教学、能力授予、事件、门、捷径及奖励仍由切片程序实现。

设计包 `chapter.example.json` 的章节 ID、起始节点、作者保护事实、节点跳转、人物别名、房间引用、条件、授予、事件引用、检查点和章节终点等尚未通过此入口驱动。完整未接字段和节点类型会写入每次映射报告。不要把 chapter 合同直接粘贴进这个三字段文件；未知字段会阻止导入。

现有 `CHAPTER_TEMPLATE.md`、`EVENT_TEMPLATE.md` 可供作者准备后续正式内容。导入器不读取或改写这些模板，也不把它们视为已实现的章节图。
