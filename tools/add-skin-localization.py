"""Merge skin page translations without rewriting existing localization entries."""
import json
from pathlib import Path
path=Path(__file__).resolve().parents[1]/'LOL-GameAssistant/Resources/locale-en.json'
raw=path.read_bytes(); existing=json.loads(raw)
entries={
    '选择皮肤与形态':'Choose skin and form',
    '游戏换肤':'Game skins', '启用独立核心':'Enable skin core',
    '搜索皮肤名称或模型':'Search skin name or model',
    '请先启用并读取当前英雄':'Enable the core and load the current champion',
    '读取当前英雄':'Load champion', '应用所选皮肤':'Apply skin', '恢复基础皮肤':'Restore base skin',
    '尚未连接游戏':'Not connected to a game',
    '功能已关闭。启用后可读取当前对局。':'Disabled. Enable to connect to the current game.',
    '功能已启用，等待读取当前对局。':'Enabled. Waiting to connect to the current game.',
    '功能已关闭；已开始的请求可能已执行，当前皮肤不会自动还原。':'Disabled. A request already started may have executed; the current skin is not automatically restored.',
    '实验性功能：游戏更新后可能需要重新适配。第三方换肤存在账号处罚风险。\n关闭开关停止新的请求，不会卸载已加载的游戏模块或自动恢复皮肤。':'Experimental: game updates may require new compatibility work. Third-party skin modification carries account penalty risks.\nDisabling stops new requests; it does not unload a loaded game module or restore the skin.',
    '个皮肤与形态':'skins and forms', '当前状态':'Current state', '形态':'Form',
    '已连接。请选择条目；状态核验不替代画面和技能验收。':'Connected. Choose an entry; state verification does not verify visuals or abilities.',
    '请求执行中…':'Executing request…',
    '调用完成，实际状态已核验。画面、特效与技能仍需检查。':'Call completed and state verified. Visuals, effects and abilities still need inspection.',
    '调用完成，但实际状态核验未通过。请刷新，勿连续重试。':'Call completed, but state verification failed. Refresh; do not repeatedly retry.',
    '核心文件缺失，请重新安装完整运行包。':'Core files are missing. Reinstall the complete package.',
    '未检测到游戏对局，请进入游戏后刷新。':'No game detected. Enter a game and refresh.',
    '当前游戏版本尚未适配，已停止调用。':'This game version is unsupported. The call was stopped.',
    '请求结果未确认，动作可能已执行。请读取状态，勿自动重试。':'The outcome is unknown; the action may have executed. Read the state instead of retrying automatically.',
    'Windows 拒绝核心连接，未继续加载。':'Windows rejected the core connection. Loading did not continue.',
    '对局已变化，请重新读取当前英雄。':'The game session changed. Load the current champion again.',
}
new={k:v for k,v in entries.items() if k not in existing}
if new:
    end=raw.rfind(b'}'); prefix=raw[:end].rstrip(); newline=b'\r\n' if b'\r\n' in raw else b'\n'
    lines=[('  '+json.dumps(k,ensure_ascii=False)+': '+json.dumps(v,ensure_ascii=False)).encode('utf-8') for k,v in new.items()]
    path.write_bytes(prefix+b','+newline+(b','+newline).join(lines)+newline+b'}'+newline)
json.loads(path.read_bytes())
