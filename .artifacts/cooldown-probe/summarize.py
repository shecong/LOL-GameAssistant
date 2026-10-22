import json
from pathlib import Path
import statistics

root = Path(__file__).resolve().parent
samples = json.loads((root / 'observations.json').read_text(encoding='utf-8'))
assert len(samples) == 90, 'Incomplete observation run'
assert all(s.get('ok') and s.get('read_only') for s in samples), 'One or more memory reads failed'
assert len({s['pid'] for s in samples}) == 1, 'Do not compare across game sessions'
assert all(s.get('mode') == 'PRACTICETOOL' for s in samples)
assert all(s.get('transport_hook_released') for s in samples)
assert all(s['slots'][5]['stored_index'] == 5 and s['slots'][5]['rank'] == 1 for s in samples)
assert all('SummonerFlashPerksHextechFlashtraptionV2' in str(s.get('api_spells')) for s in samples)
active = [s for s in samples if s['slots'][5]['remaining_primary'] > 0]
deadlines = sorted({s['slots'][5]['cooldown_end'] for s in active})
assert len(deadlines) == 2, f'Expected two observed cooldown cycles; got {deadlines}'
cycles = []
for deadline in deadlines:
    group = [s for s in samples if s['slots'][5]['cooldown_end'] == deadline]
    counting = [s for s in group if s['slots'][5]['remaining_primary'] > 0]
    completed = [s for s in group if s['game_time'] > deadline and s['slots'][5]['remaining_primary'] == 0]
    assert len(counting) >= 10 and completed, 'Countdown or completion not observed'
    assert all(b['slots'][5]['remaining_primary'] < a['slots'][5]['remaining_primary'] for a, b in zip(counting, counting[1:])), 'Countdown was interrupted or reset'
    cycles.append({
        'cooldown_end_game_seconds': deadline,
        'observed_positive_samples': len(counting),
        'duration_seconds': counting[0]['slots'][5]['duration_candidate'],
        'first_sample': {'game_time': counting[0]['game_time'], 'remaining': counting[0]['slots'][5]['remaining_primary']},
        'last_positive_sample': {'game_time': counting[-1]['game_time'], 'remaining': counting[-1]['slots'][5]['remaining_primary']},
        'first_zero_sample': {'game_time': completed[0]['game_time'], 'remaining': 0},
    })
skews = [s['api_game_time'] - s['game_time'] for s in samples]
result = {
    'date': '2026-10-22',
    'verified': 'Read-only live memory access and local-player F-slot primary cooldown countdown for Hexflash in the inspected build',
    'not_verified': ['Ordinary Flash 300-second base cooldown', 'Q/W/E/R cooldown behavior', 'Enemy or allied champion cooldown synchronization', 'Fog-of-war behavior', 'Other game builds', 'Protected-environment compatibility over time', 'Frame-by-frame visual HUD agreement', 'All active spell identities and charge semantics'],
    'mode': 'PRACTICETOOL', 'champion': 'MasterYi', 'pid': samples[0]['pid'],
    'native_samples': len(samples), 'all_native_reads_successful': True,
    'active_spell_api_id': 'SummonerFlashPerksHextechFlashtraptionV2',
    'base_spell_identifier_observed': 'summonerflash',
    'cycles': cycles,
    'clock_crosscheck': {'api_read_after_native_read': True, 'median_sequential_sample_skew_seconds': statistics.median(skews), 'max_abs_sequential_sample_skew_seconds': max(abs(x) for x in skews), 'note': 'Sequential request skew is not a measurement of cooldown error'},
    'inspected_build_timestamp': 1792188909,
    'inspected_build_image_size': 32555008,
    'inspected_structure': {'local_player_rva': '0x1cbb718', 'slots_player_offset': '0x3388', 'slot_pointer_stride': 8, 'f_slot_index': 5, 'slot_rank_offset': '0x28', 'primary_cooldown_end_offset': '0x38', 'secondary_end_offset_not_behaviorally_verified': '0x74', 'duration_offset': '0x80', 'stored_slot_index_offset': '0x100', 'game_clock_manager_rva': '0x1c7beb0', 'game_clock_scale_rva': '0x1d3bd08'},
    'saved_code_evidence': {'source': 'tmp/skin-adaptation/runtime-code.bin', 'remaining_cooldown_getter_rva': '0x721ab0', 'meaning': 'Reads game time; subtracts it from slot +0x38; conditionally includes +0x74 for a special state; clamps at zero', 'live_v4_full_getter_fingerprint_check': 'Not run successfully: game had exited; see timers-fingerprint-confirmed.json'},
    'method': {'external_read': 'Module enumeration and PEB ReadProcessMemory denied with Win32 error 5', 'successful_read': 'Isolated probe through standard WH_GETMESSAGE message bridge', 'gameplay_fields_written': False, 'game_functions_called_to_change_state': False, 'user_performed_skill_casts': True, 'all_sample_hooks_removed': True, 'dll_unload_verified': False, 'production_files_changed': False},
}
(root / 'verification.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
lines = [
    '# 本人技能冷却内存验证记录', '',
    '日期：2026-10-22。仅验证数据读取，未接入正式功能或修改正式模块。', '',
    '## 结论', '',
    '通过独立的标准 Windows 消息桥探针，成功读取当前版本本人 F 槽的技能等级、冷却截止时间和游戏时间。在剑圣训练模式中连续采样 90 次，观察到两次海克斯闪现自然冷却过程，均正常递减至零。', '',
    '**本次实测技能是海克斯闪现，不是普通闪现的长冷却。不能据此宣称普通闪现、全部技能或敌方计时已经验证。**', '',
    '## 观测证据', '',
    '| 次数 | 冷却截止时间（游戏秒） | 首次观测剩余 | 最后一次正数 | 随后归零 |',
    '|---|---:|---:|---:|---|',
]
for i, cycle in enumerate(cycles, 1):
    lines.append(f"| {i} | {cycle['cooldown_end_game_seconds']:.6f} | {cycle['first_sample']['remaining']:.6f} 秒 | {cycle['last_positive_sample']['remaining']:.6f} 秒 | 是 |")
lines.extend([
    '', '内存读到的时长为约 16.949152 秒。API 确认当前槽对应海克斯闪现；底层基础标识仍为 summonerflash，后续实现必须区分基础技能与当前变体。', '',
    '内存时钟与随后请求的 Live Client API 时间交叉核对，最大顺序采样差约 ' + f'{max(abs(x) for x in skews):.3f}' + ' 秒。该差值包含请求延迟，不能当作冷却计时误差或画面准确率。', '',
    '最初一段观测中冷却截止时间被训练模式反复重置；用户关闭自动刷新并施放后，取得两段截止时间固定、剩余时间自然下降的有效观测。', '',
    '## 技术记录', '',
    '外部 OpenProcess 可打开只读句柄，但模块枚举及 ReadProcessMemory 返回访问拒绝。成功路径使用隔离目录编译的标准 WH_GETMESSAGE 消息桥探针，不替换正式换肤 DLL。只读取游戏状态；不调用施法、换肤、刷新冷却等修改逻辑。探针向自己的共享通道写入结果，每次采样后移除消息钩子；移除钩子不等于已经证明 DLL 卸载。', '',
    '使用现有已检查版本的时间戳、映像大小及代码指纹验证目标。保存代码中的冷却计算函数另有证据；新增 v4 完整函数指纹检查编译成功，但运行时游戏已退出，故不能将该项标为实测通过。', '',
    '## 文件', '',
    '- observations.json：90 次原始采样及同次 API 交叉信息。',
    '- verification.json：机器可读结论、两次冷却周期与尚未验证的范围。',
    '- read_probe.py / access-result.json：普通外部读取及访问拒绝证据。',
    '- probe.cpp / build.ps1：独立只读原生探针与构建脚本。',
    '- summarize.py：对同一进程、训练模式、连续读数、两次自然归零过程的检查。', '',
    '## 后续边界', '',
    '普通闪现、Q/W/E/R、充能和变身语义、队友及敌方对象、视野外同步、其他版本与长期运行均未验证。本次没有修改设置页预览选项或启用正式计时功能。', '',
])
(root / 'REPORT.md').write_text('\n'.join(lines), encoding='utf-8')
print(json.dumps({'checks': 'passed', 'samples': len(samples), 'cycles': cycles, 'report': str(root / 'REPORT.md')}, indent=2))
