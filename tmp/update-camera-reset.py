from pathlib import Path
p=Path('native/LeagueSkinCore/native/camera_core.h');s=p.read_text(encoding='utf-8');s=s.replace('\\"camera_distance\\":%.9g,','\\"camera_default_view_distance\\":%.9g,\\"camera_distance\\":%.9g,');s=s.replace('r->nonce,identity,(double)value','r->nonce,identity,(double)defaultView,(double)value');p.write_text(s,encoding='utf-8')
# Rename reset semantics in managed callers, including fakes/tests.
for p in [Path('LOL-GameAssistant/Application/GameData/IGameOverlayBridge.cs'),Path('LOL-GameAssistant/Infrastructure/GameData/NativeGameOverlayBridge.cs'),Path('LOL-GameAssistant/BaseViewForm/SkinToolsForm.OtherSettings.cs'),Path('LOL-GameAssistant.UiTests/GameOverlayProtocolTests.cs'),Path('LOL-GameAssistant.UiTests/GameOverlayPanelsTests.cs'),Path('LOL-GameAssistant.UiTests/SkinCameraUiTests.cs')]:
 s=p.read_text(encoding='utf-8').replace('ReadCameraMoveTo600','ReadCameraReset').replace('MoveCameraTo600Async','RestoreDefaultCameraAsync');p.write_text(s,encoding='utf-8')

