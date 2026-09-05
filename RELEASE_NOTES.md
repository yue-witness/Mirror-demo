# PROJECT MIRROR Demo v0.1.4

本版本调整玩家界面、补足后续对局语音，并整理当前 demo 的代码职责。

- 修复 TUTOR 图集重复显示造成的重影；头像保留单一圆框和局部红眼异常。
- 为已有录音的 59 条后续对局反馈启用语音，覆盖选择、修改选择、犹豫、行动后和接近终局等时机。普通反馈排队播放，避免相互打断；沿用现有声线，没有新增合成录音。
- 去除剩余完成局数、会话编号等冗余显示，重排规则、局面、操作、TUTOR 对话和选择历史。Bash 与 Limit Bash 均显示可恢复的本局历史。
- 使用较柔和的蓝色主题，放大主要文字；正文与按钮使用原生字形，标题减弱点阵效果。边框拖尾速度降至原来的四分之一。
- 将默认图像、音效、颜色、布局与特效参数放入场景和资源，可在 Godot 编辑器中调整。
- 按 Application、Domain、Narrative、Infrastructure、Presentation 整理代码，移出废弃内容，新增 [架构说明](ARCHITECTURE.md)。

## 安装

下载 Windows x64 ZIP，完整解压后运行 `PROJECT_MIRROR_Demo.exe`。包内包含所需 .NET 运行时，无需单独安装。SHA256SUMS.txt 用于校验 ZIP。

发行包不包含开发测试、QA 截图、编辑器缓存或玩家进度。存档保持 schema 3；旧 Bash 存档可以继续游戏，但无法重建旧版本未记录的行动历史。

## 验证结果

Release 构建零警告 / 零错误；1086 项领域断言、Godot 资源导入扫描，以及 UiRefinementSmoke、TutorSpeechSmoke、TutorPortraitVisualSmoke、FullFlowSmoke 均通过。完整流程完成最终结算并返回标题。

487 个 PCK 资源和 190 个运行文件通过包内容检查；解压副本逐文件校验一致。EXE 使用默认 D3D12 与 OpenGL 均启动成功，退出码为 0，日志无警告 / 错误，原有玩家存档未被改动。
