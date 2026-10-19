const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'..');
const source=fs.readFileSync(path.join(__dirname,'lcu-docs-index.html'),'utf8');
const clean=s=>(s||'').replace(/<script[\s\S]*?<\/script>/gi,'').replace(/<style[\s\S]*?<\/style>/gi,'').replace(/<[^>]*>/g,' ').replace(/&nbsp;/g,' ').replace(/&amp;/g,'&').replace(/&lt;/g,'<').replace(/&gt;/g,'>').replace(/&quot;/g,'"').replace(/&apos;|&#39;/g,"'").replace(/\s+/g,' ').trim();
const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const groups={};
function moduleDef(name,label,about,rules){groups[name]={label,about,rules:{}};for(const line of rules.trim().split('\n')){if(!line.trim())continue;const i=line.indexOf('=');if(i<0)throw Error(line);const key=line.slice(0,i).trim();if(key in groups[name].rules)throw Error('duplicate rule '+name+' '+key);groups[name].rules[key]=line.slice(i+1).trim();}}
moduleDef('async','异步任务','异步操作的完成结果、状态与取消。',`
result/:=已完成异步任务的结果
status/:=异步任务的执行状态或完成记录
`);
moduleDef('cookie-jar','Cookie 存储','客户端内部 Cookie 容器。',`cookies=客户端 Cookie 集合`);
moduleDef('data-store','本地数据存储','客户端安装目录及安装级、系统级键值设置。',`
install-dir=客户端安装目录
install-settings/:=指定键的安装级设置
system-settings/:=指定键的系统级设置
`);
moduleDef('entitlements','权益令牌','身份认证相关的权益令牌。',`token=权益认证令牌`);
moduleDef('voice-chat','底层语音服务','语音设备、连接会话、编解码、按键说话及通话统计。',`
audio-properties=音频属性
call-stats/aggregate=所有通话的汇总统计
call-stats/:=指定通话的统计
codec-settings=音频编解码设置
config=底层语音服务配置
errors=底层语音服务错误
push-to-talk=按键说话配置
push-to-talk/check-available=!检查按键说话功能或按键是否可用
sessions=语音会话集合
sessions/:=指定语音会话
devices/capture=音频输入设备列表
devices/capture/permission=麦克风访问权限状态
devices/capture/prompt-for-permission=!请求显示麦克风访问授权提示
devices/render=音频输出设备列表
sessions/:/participants/:=指定语音会话中的指定参与者属性
settings=底层语音设置
state=底层语音服务状态
`);
moduleDef('lol-account-verification','账号验证','账号验证流程、设备标识及验证码。',`
authenticate=!发起账号验证身份认证
device=账号验证使用的设备信息
invalidate=!使当前账号验证状态失效
is-verified=当前账号是否已验证
send-token=!发起发送账号验证码的请求
verify=!提交验证码以完成账号验证
`);
moduleDef('lol-acs','历史战绩数据服务','ACS 战绩、时间线、比赛列表和最近使用英雄，属于历史接口层。',`
acs-endpoint-override=!覆盖 ACS 数据服务端点配置
delta=ACS 战绩数据增量
games/:=指定比赛的详细战绩
gametimelines/:=指定比赛的事件时间线
matchlists/:=指定账号的比赛列表
recently-played-champions/:=指定账号最近使用的英雄
matchlists=比赛列表
recently-played-champions/current-summoner=当前玩家最近使用的英雄
request-recently-played-champions/current-summoner=!请求加载当前玩家最近使用的英雄数据
request-recently-played-champions/:=!请求加载指定账号最近使用的英雄数据
`);
moduleDef('lol-active-boosts','生效中的加成','玩家当前激活的经验等加成。',`active-boosts=当前生效的加成列表`);
moduleDef('lol-banners','玩家旗帜','玩家展示旗帜及边框装备信息。',`
current-summoner/flags=当前玩家拥有或可用的旗帜
current-summoner/flags/equipped=当前玩家装备的旗帜
current-summoner/frames/equipped=当前玩家装备的旗帜边框
players/:/flags/equipped=指定玩家装备的旗帜
`);
moduleDef('lol-career-stats','生涯与对比统计','玩家、英雄和位置的聚合数据及统计百分位。',`
champion-averages/season/:/:/:/:/:=指定赛季、英雄、位置、段位和队列的英雄平均统计
champion-averages/:/:/:/:=指定英雄、位置、段位和队列的英雄平均统计
champion-experts/season/:/:/:=指定赛季、英雄和位置的高手参考数据
champion-experts/:/:=指定英雄和位置的高手参考数据
champion-stats-percentiles=!计算或请求英雄统计的百分位参考值
position-averages/season/:/:/:/:=指定赛季、位置、段位和队列的位置平均统计
position-averages/:/:/:=指定位置、段位和队列的位置平均统计
position-experts/season/:/:=指定赛季和位置的高手参考数据
position-experts/:=指定位置的高手参考数据
position-stats-percentiles=!计算或请求位置统计的百分位参考值
summoner-games/:=指定玩家用于生涯分析的比赛样本
summoner-games/:/season/:=指定玩家在指定赛季的比赛样本
summoner-stats/:/:/:/:=指定玩家、赛季、队列和位置的生涯统计
`);
moduleDef('lol-catalog','物品目录','按库存类别查询商品或物品定义。',`items/:=指定库存类别的目录条目`);
moduleDef('lol-champ-select','英雄选择','选人会话、可选英雄、选禁操作、备战席、交换与团队加成。',`
bannable-champions=当前可禁用的英雄 ID 列表
battle-training/launch=!请求启动选人相关的战斗训练流程
current-champion=本地玩家当前选择的英雄 ID
implementation-active=该选人实现是否处于启用状态
disabled-champions=当前被禁用或不可用的英雄列表
pickable-champions=当前可选择的英雄 ID 列表
pickable-skins=当前选人阶段可选择的皮肤列表
retrieve-latest-game-dto=!请求刷新最新的选人比赛数据对象
session=当前选人会话，通常包含双方阵容、行动序列及本地位置
session/actions/:=指定选人行动的内容；PATCH 常用于设置该行动的英雄或完成状态，实际字段以 schema 为准
session/actions/:/complete=!提交完成指定选禁行动的请求
session/bench/swap/:=!请求将当前英雄换成备战席中的指定英雄
session/my-selection=本地玩家的选人配置，例如英雄、召唤师技能或皮肤等字段；允许字段以当前 schema 为准
session/my-selection/reroll=!请求在允许重随机的模式中重新随机英雄
session/simple-inventory=!提交或刷新当前选人会话使用的精简库存数据
simple-inventory=!提交或刷新选人模块的精简库存数据
session/timer=选人各阶段的计时信息
session/trades=当前英雄交换请求列表
session/trades/:=指定英雄交换请求的状态
session/trades/:/accept=!接受指定的英雄交换请求
session/trades/:/cancel=!取消指定的英雄交换请求
session/trades/:/decline=!拒绝指定的英雄交换请求
session/trades/:/request=!向指定交换对象发起英雄交换请求
team-boost=当前选人会话的团队加成信息
team-boost/purchase=!提交购买团队加成的请求
has-auto-assigned-smite=是否已自动分配惩戒
preferences=选人偏好设置
sending-loadouts-gcos-enabled=是否启用向 GCOS 发送配置方案的功能开关；GCOS 具体职责原文未说明
`);
moduleDef('lol-champions','英雄与皮肤库存','指定玩家的英雄、皮肤、炫彩及可玩数量。',`
inventories/:/champions=指定玩家的英雄库存及英雄信息
inventories/:/champions-minimal=指定玩家的精简英雄库存
inventories/:/champions-playable-count=指定玩家可游玩的英雄数量
inventories/:/champions/:=指定玩家库存中的单个英雄信息
inventories/:/champions/:/skins=指定玩家某英雄的皮肤列表
inventories/:/champions/:/skins/:=指定玩家某英雄的指定皮肤信息
inventories/:/champions/:/skins/:/chromas=指定玩家某英雄皮肤的炫彩列表
inventories/:/skins-minimal=指定玩家的精简皮肤库存
owned-champions-minimal=当前玩家已拥有英雄的精简列表
`);
moduleDef('lol-chat','聊天与好友','好友、分组、好友请求、黑名单、会话及聊天状态。',`
blocked-players=聊天黑名单集合
blocked-players/:=指定被屏蔽玩家的黑名单记录
config=聊天服务配置
conversations=聊天会话集合
conversations/active=当前激活的聊天会话
conversations/notify=聊天会话通知状态；具体通知语义原文未说明
conversations/:=指定聊天会话的属性
conversations/:/closed=指定聊天会话的关闭状态
conversations/:/messages=指定聊天会话的消息；POST 用于发送消息
conversations/:/participants=指定聊天会话的参与者
errors=聊天错误列表
errors/:=指定聊天错误记录
friend-counts=好友数量及相关分组统计
friend-groups=好友分组集合
friend-groups/:=指定好友分组
friend-groups/:/friends=指定分组中的好友列表
friend-requests=好友申请集合
friend-requests/:=指定好友申请；PUT 的接受或更新字段需查当前 schema，不能仅凭方法确定
friends=好友列表及在线状态
friends/:=指定好友的资料或关系记录
me=当前玩家在聊天服务中的资料、在线状态等属性
session=聊天连接会话
session/plain=!使用普通认证方式建立聊天会话；参数格式原文未给出
session/rso=!使用 Riot 单点认证信息建立聊天会话
settings=聊天设置集合
settings/:=指定键的聊天设置
`);
moduleDef('lol-clash','Clash 冠军杯赛','Clash 赛事、战队名单、报名锁定、替补、门票、侦察及奖励。',`
bracket/:=指定淘汰赛分组或对阵树
checkin-allowed=当前是否允许进行赛事签到
enabled=Clash 功能是否启用
eogPlayerUpdate=Clash 赛后玩家更新数据
eogPlayerUpdate/acknowledge=!确认已处理 Clash 赛后玩家更新
event/:=指定 UUID 的 Clash 事件
events=!提交 Clash 事件；事件类型与载荷原文未说明
gameEnd=Clash 单场比赛结束状态
gameEnd/acknowledge=!确认已处理 Clash 比赛结束信息
historyandwinners=Clash 历史赛事及获胜者
iconconfig=Clash 队徽或图标配置
invited-roster-ids=邀请当前玩家加入的战队名单 ID
notifications=Clash 通知列表
notifications/acknowledge=!确认已处理 Clash 通知
ping=Clash 模块连通性或心跳信息；具体响应原文未说明
player=当前玩家的 Clash 参赛资料
player/chat-rosters=当前玩家参与聊天的 Clash 战队名单
player/history=当前玩家的 Clash 参赛历史
playmode-restricted=当前玩家是否受 Clash 模式准入限制
ready=Clash 模块是否就绪
refresh=!请求刷新 Clash 数据
rewards=Clash 奖励信息
roster/:=指定 Clash 战队的名单及资料
roster/:/accept=!接受加入指定 Clash 战队的邀请
roster/:/cancel-withdraw=!请求取消指定 Clash 战队的退赛流程；与 unwithdraw 的区别原文未说明
roster/:/change-all-details=!批量修改指定 Clash 战队的展示资料
roster/:/change-icon=!修改指定 Clash 战队的队徽
roster/:/change-name=!修改指定 Clash 战队的名称
roster/:/change-short-name=!修改指定 Clash 战队的简称
roster/:/decline=!拒绝加入指定 Clash 战队的邀请
roster/:/disband=!解散指定 Clash 战队
roster/:/invite=!邀请玩家加入指定 Clash 战队
roster/:/kick=!从指定 Clash 战队移除成员
roster/:/leave=!请求离开指定 Clash 战队
roster/:/lockin=!锁定指定 Clash 战队的参赛阵容
roster/:/set-position=!设置指定 Clash 战队成员的位置
roster/:/set-ticket=!设置指定 Clash 战队参赛使用的门票
roster/:/stats=指定 Clash 战队的统计数据
roster/:/substitute/accept=!接受指定 Clash 战队的替补邀请
roster/:/substitute/decline=!拒绝指定 Clash 战队的替补邀请
roster/:/substitute/invite=!邀请玩家作为指定 Clash 战队的替补
roster/:/substitute/reclaim=!请求收回指定 Clash 战队的替补占位或恢复原位置；具体规则原文未说明
roster/:/substitute/:/revoke=!撤销指定玩家的 Clash 替补资格或邀请
roster/:/ticket-offer/:/accept=!接受指定玩家相关的 Clash 门票赠送提议
roster/:/ticket-offer/:/decline=!拒绝指定玩家相关的 Clash 门票赠送提议
roster/:/ticket-offer/:/offer=!向指定玩家发起 Clash 门票赠送提议
roster/:/ticket-offer/:/revoke=!撤销向指定玩家发起的 Clash 门票赠送提议
roster/:/transfer-captain=!转让指定 Clash 战队的队长身份
roster/:/unlockin=!取消指定 Clash 战队的阵容锁定
roster/:/unwithdraw=!请求恢复已退出的 Clash 战队参赛状态；与 cancel-withdraw 的区别原文未说明
roster/:/update-logos=!更新指定 Clash 战队的队徽资源
roster/:/withdraw=!请求指定 Clash 战队退赛
scouting/champions=Clash 赛前侦察所用的英雄数据
scouting/matchhistory=Clash 赛前侦察所用的比赛记录
simple-state-flags/:/acknowledge=!确认指定 Clash 简单状态标记
thirdparty/team-data=供第三方集成使用的 Clash 战队数据
time=Clash 服务的时间信息
tournament-state-info=Clash 赛事状态信息
tournament-summary=Clash 赛事概要
tournament/cancelled=Clash 赛事取消状态
tournament/:=指定 Clash 赛事详情
tournament/:/create-roster=!为指定 Clash 赛事创建战队名单
tournament/:/player=当前玩家在指定 Clash 赛事中的参赛信息
tournament/:/player-honor-restricted=当前玩家是否因荣誉条件被限制参加指定 Clash 赛事
tournament/:/stateInfo=指定 Clash 赛事的状态信息
tournament/:/winners=指定 Clash 赛事的获胜战队
tournamentIds=Clash 赛事 ID 列表
update-logos=!更新 Clash 队徽资源
visible=Clash 入口或页面的可见状态
voice=Clash 语音连接
voice-delay/:=延迟指定秒数的 Clash 语音连接操作
voice-enabled=Clash 语音功能是否启用
`);
moduleDef('lol-clubs-public','公开俱乐部资料','历史俱乐部的公开资料与标签。',`
clubs/public=可访问的公开俱乐部资料
clubs/public/:=指定玩家关联的公开俱乐部资料
clubs/public/:/tag=指定玩家的公开俱乐部标签
`);
moduleDef('lol-clubs','俱乐部管理','历史俱乐部的成员、邀请、公告、提名及权限管理。',`
clubs=当前可访问的俱乐部列表
clubs/invitations=当前玩家收到的俱乐部邀请
clubs/membership=当前玩家的俱乐部成员关系
clubs/membership/preferences=当前玩家的俱乐部成员偏好
clubs/:=指定俱乐部的资料
clubs/:/invitations=指定俱乐部的邀请列表
clubs/:/invitations/:=指定俱乐部对指定玩家的邀请
clubs/:/members=指定俱乐部的成员列表
clubs/:/members/:=指定俱乐部与指定玩家的成员关系
clubs/:/motd=指定俱乐部的当日公告
clubs/:/nominations=指定俱乐部的成员提名列表
clubs/:/nominations/:=指定俱乐部对指定玩家的成员提名
clubs/:/promotions/:=指定俱乐部中指定玩家的晋升或管理权限记录
clubs/:/view=!记录指定俱乐部已被查看
`);
moduleDef('lol-collections','收藏品与旧符文册','英雄成就、头像、守卫皮肤、召唤师技能与历史符文册。',`
inventories/chest-eligibility=收藏品或英雄相关的宝箱获取资格
inventories/:/backdrop=指定玩家的个人展示背景
inventories/:/champion-mastery=指定玩家的英雄成就数据
inventories/:/champion-mastery/top=指定玩家成就最高的英雄
inventories/:/rune-book=指定玩家的历史符文册
inventories/:/rune-book/pages/:=指定玩家的指定历史符文页
inventories/:/rune-book/select-page/:=!将指定历史符文页设为所选页面
inventories/:/runes=指定玩家的历史符文库存
inventories/:/spells=指定玩家可用的召唤师技能
inventories/:/summoner-icons=指定玩家的召唤师头像列表
inventories/:/verification=!更新或提交指定玩家收藏品库存的校验信息
inventories/:/ward-skins=指定玩家的守卫皮肤列表
inventories/:/ward-skins/:=指定玩家的指定守卫皮肤信息
inventories/:/summoner-icons/:=指定玩家的指定召唤师头像信息
`);
moduleDef('lol-content-targeting','内容定向','按地区和语言等条件选择客户端展示内容。',`filters=内容定向筛选条件
locale=内容定向所用的语言区域`);
moduleDef('lol-email-verification','电子邮箱验证','账号邮箱地址与确认流程。',`confirm-email=!发起确认账号邮箱的请求
email=账号验证邮箱地址或相关状态`);
moduleDef('lol-end-of-game','赛后结算','赛后成绩、英雄成就变化、举报记录与关闭结算。',`
champion-mastery-updates=赛后的英雄成就更新
eog-stats-block=赛后结算成绩数据块
reported-players=本次结算中已举报的玩家
state/dismiss-stats=!关闭或确认赛后统计页面
player-complaints=!提交对玩家的举报或投诉
`);
moduleDef('lol-esport-stream-notifications','电竞直播提醒','直播列表、直播链接和相关统计。',`live-streams=当前电竞直播列表
send-stats=!提交电竞直播提醒相关的统计事件
stream-url=电竞直播跳转地址`);
moduleDef('lol-featured-modes','轮换与活动模式','限时活动模式入口及索引。',`locked-event/active=锁定活动或轮换模式的激活状态
locked-event/index=锁定活动或轮换模式的索引信息`);
moduleDef('lol-game-client-chat','游戏进程聊天桥接','游戏内好友、屏蔽和聊天消息桥接。',`buddies=游戏内可用的好友列表
ignored-summoners=游戏内被忽略或屏蔽的召唤师集合
instant-messages=!发送或转交即时聊天消息
party-messages=!发送或转交队伍聊天消息`);
moduleDef('lol-game-queues','游戏队列配置','排位、匹配和自定义模式的队列及规则。',`custom=默认自定义游戏队列配置
custom-non-default=非默认自定义游戏队列配置
game-type-config/:=指定游戏类型的规则配置
game-type-config/:/map/:=指定地图下的游戏类型规则配置
queues=全部可见游戏队列配置
queues/type/:=指定队列类型的配置
queues/:=指定队列 ID 的配置`);
moduleDef('lol-game-session','游戏会话桥接','游戏连接、重连信息及内部回声测试。',`clearInfo=!清理游戏会话信息；虽然使用 GET，路径表示可能有副作用
echo=本地游戏会话回声测试信息
reconnectInfo=当前游戏会话的重连信息
remote-echo=远端游戏会话回声测试信息`);
moduleDef('lol-game-settings','游戏设置','游戏设置与按键设置，以及各自的结构定义。',`didreset=游戏设置是否被重置
game-settings=游戏内设置集合
game-settings-schema=游戏内设置的结构定义
input-settings=按键及输入设置
input-settings-schema=按键及输入设置的结构定义
ready=游戏设置模块是否就绪
reload-post-game=!赛后重新加载游戏设置
save=!保存当前游戏设置`);
moduleDef('lol-gameflow','游戏生命周期','大厅、匹配、选人、游戏中、重连、观战及赛后的流程状态。',`
ack-failed-to-launch=!确认已处理游戏启动失败事件
active-patcher-lock=当前影响游戏流程的更新器锁
availability=游戏流程模块当前可用性
basic-tutorial=基础教程状态
basic-tutorial/start=!启动基础教程流程
battle-training=战斗训练流程状态
battle-training/start=!启动战斗训练流程
battle-training/stop=!停止战斗训练流程
client-received-message=!向流程模块报告客户端已收到消息
extra-game-client-args=启动游戏进程使用的额外命令行参数
gameflow-metadata/player-status=游戏流程元数据中的玩家状态
gameflow-metadata/registration-status=游戏流程元数据中的注册或报名状态
gameflow-phase=当前游戏生命周期阶段，例如大厅、选人或游戏中；具体枚举需看当前版本
pre-end-game-transition=!触发进入赛前结算过渡阶段的流程，具体阶段定义原文未给出
reconnect=!请求重新连接正在进行的游戏
session=完整游戏流程会话，通常包含比赛、队列与状态信息
session/dodge=!请求退出当前选人或游戏进入流程，可能涉及秒退判定
session/event=!向游戏流程会话提交内部事件
session/game-configuration=!向游戏流程会话提交比赛配置
session/request-enter-gameflow=!请求进入游戏生命周期流程
session/request-lobby=!请求进入或返回大厅流程
session/request-tournament-checkin=!请求进入赛事签到流程
session/tournament-ended=!向游戏流程报告赛事已结束
spectate=当前观战流程信息
spectate/delayed-launch=延迟启动观战的状态或配置
spectate/launch=!请求启动观战流程
spectate/quit=!请求退出观战流程
tick=!触发游戏流程的一次内部更新周期
watch=观看流程的状态或参数；与 spectate 的区别原文未说明
watch/launch=!启动 watch 观看流程；具体观看对象由请求参数决定
`);
moduleDef('lol-geoinfo','地域信息','客户端或账号相关的地域定位信息。',`getlocation=地理位置相关信息；精度和字段原文未说明
whereami=当前地域或区域归属信息；与 getlocation 的差别原文未说明`);
moduleDef('lol-highlights','精彩时刻','本地精彩片段的索引、元数据与保存路径。',`config=精彩时刻功能配置
file-browser/:=!在文件管理器中定位指定精彩片段
highlights=精彩片段集合
highlights-folder-path=当前精彩片段保存目录
highlights-folder-path/default=默认精彩片段保存目录
highlights/:=指定精彩片段的记录或元数据；删除是否包含文件需核对当前实现`);
moduleDef('lol-honor-v2','荣誉系统','赛后点赞投票、荣誉资料、等级和奖励通知。',`ballot=当前荣誉投票候选及投票信息
config=荣誉系统配置
honor-player=!提交对指定队友的荣誉投票
late-recognition=延迟到达的荣誉认可信息
late-recognition/ack=!确认已看到延迟荣誉认可
latest-eligible-game=最近一场有资格参与荣誉投票的比赛
level-change=荣誉等级变化信息
level-change/ack=!确认已看到荣誉等级变化
mutual-honor=互相授予荣誉的信息
mutual-honor/ack=!确认已看到互相授予荣誉的信息
profile=当前玩家的荣誉资料
recognition=荣誉认可或受赞信息
reward-granted=已发放的荣誉奖励信息
reward-granted/ack=!确认已看到荣誉奖励
team-choices=队伍相关的荣誉投票选项或选择状态
vote-completion=荣誉投票完成状态`);
moduleDef('lol-inventory','统一库存与钱包','玩家库存、库存通知、签名库存和虚拟货币余额。',`
champSelectInventory=选人阶段使用的库存数据
initial-configuration-complete=库存初始配置是否完成
inventory=当前玩家的统一库存
inventory/emotes=当前玩家的表情库存
notification/acknowledge=!确认已处理库存通知
notifications/:=指定库存类型的通知
players/:/inventory=指定玩家的库存信息
signedInventory=带签名的库存数据
signedInventory/tournamentlogos=带签名的赛事队徽库存
signedInventoryCache=签名库存缓存
signedWallet=带签名的钱包数据
signedWallet/:=指定币种的签名钱包数据
wallet=当前玩家的虚拟货币钱包余额
wallet/:=指定币种的钱包余额
inventory/:=指定类别的玩家库存
`);
moduleDef('lol-item-sets','自定义装备方案','玩家的自定义装备集合，用于游戏内推荐装备页。',`item-sets/:/sets=指定玩家的自定义装备方案集合`);
moduleDef('lol-kickout','强制下线通知','客户端强制下线相关提示。',`notification=强制下线通知`);
moduleDef('lol-kr-playtime-reminder','韩国地区时长提醒','历史韩国地区游戏时长与提醒。',`message=韩国地区游戏时长提醒文案
playtime=韩国地区相关的游戏时长数据`);
moduleDef('lol-kr-shutdown-law','韩国地区时段限制','历史地区政策相关的游戏时段与队列准入状态。',`custom-status=自定义游戏在地区时段限制下的状态
disabled-queues=因地区时段限制而禁用的队列
notification=地区时段限制通知
queue-status/:=指定队列的地区时段限制状态
status=地区时段限制总体状态`);
moduleDef('lol-leaver-buster','挂机与离开惩罚通知','LeaverBuster 惩罚及通知记录；不是解除惩罚接口。',`notifications=挂机或离开惩罚通知集合
notifications/:=指定挂机或离开惩罚通知记录`);
moduleDef('lol-license-agreement','用户协议','协议内容及接受、拒绝流程。',`agreements=当前需要处理的用户协议
agreements/:/accept=!提交接受指定用户协议的决定
agreements/:/decline=!提交拒绝指定用户协议的决定
all-agreements=全部可访问的用户协议`);
moduleDef('lol-loadouts','综合配置方案','按账号或其他作用域保存的配置方案。',`loadouts=综合配置方案集合
loadouts/scope/account=账号作用域的综合配置方案
loadouts/scope/:/:=指定作用域及对象的综合配置方案
loadouts/:=指定 ID 的综合配置方案`);
moduleDef('lol-lobby-team-builder','旧组队与选人桥接','历史组队服务实现，包含选人子模块和匹配操作。',`
invitations/accept=!接受组队服务的大厅邀请
lobby=组队服务中的大厅会话
lobby/countdown=组队大厅倒计时
lobby/members/:/kick=!将指定成员移出组队大厅
lobby/members/:/promote=!提升指定大厅成员的权限或转让队长身份
matchmaking=组队服务的匹配状态
matchmaking/low-priority-queue/abandon=!放弃低优先级队列中的匹配等待
matchmaking/search=!请求开始组队服务中的匹配搜索
position-preferences=!设置组队服务中的首选和次选位置
ready-check/accept=!接受组队服务的匹配确认
ready-check/decline=!拒绝组队服务的匹配确认
tb-enabled-features=组队服务已启用的功能开关
`);
moduleDef('lol-lobby','大厅与组队','创建大厅、邀请、成员权限、自定义房间、位置偏好和匹配。',`
autofill-displayed=补位提示是否已展示的标记
clash=与 Clash 相关的大厅或组队流程
custom-games=可见自定义房间列表
custom-games/refresh=!刷新可见自定义房间列表
custom-games/:=指定自定义房间的信息
custom-games/:/join=!加入指定自定义房间
lobby/availability=大厅功能的可用性
lobby/countdown=大厅进入下一阶段的倒计时
lobby/custom/bots=自定义大厅的电脑玩家集合
lobby/custom/bots/:=指定内部名称的自定义电脑玩家
lobby/custom/cancel-champ-select=!取消自定义房间的选人阶段
lobby/custom/start-champ-select=!启动自定义房间的选人阶段
lobby/custom/switch-teams=!在自定义房间切换队伍
lobby/invitations=当前大厅发出的邀请集合
lobby/invitations/:=指定大厅邀请的信息
lobby/members/localMember/position-preferences=本地大厅成员的首选和次选位置
parties/active=当前激活的组队会话
parties/health=组队服务健康状态
parties/metadata=组队会话元数据
parties/player=组队服务中的玩家信息
parties/queue=组队队列选择
parties/ready=组队准备状态
parties/:/members/:/role=指定队伍中指定玩家的角色或权限
party-rewards=组队奖励信息
tournaments/:/join=!加入指定锦标赛大厅或赛事
comms/members=大厅通信服务中的成员列表
comms/token=大厅通信服务的令牌
eligibility/game-select-eligibility-hash=游戏模式选择资格的校验哈希
eligibility/initial-configuration-complete=大厅准入条件初始配置是否完成
eligibility/party=!计算或提交整个队伍的游戏准入资格检查
eligibility/self=!计算或提交本地玩家的游戏准入资格检查
eog-invitations=!发起赛后再次组队的邀请
lobby=当前大厅会话；POST 通常用于创建或更换大厅，DELETE 用于离开或销毁本地大厅会话
lobby/custom/available-bots=自定义大厅可添加的电脑英雄
lobby/custom/bots-enabled=自定义大厅是否允许添加电脑玩家
lobby/matchmaking/search=当前大厅的匹配搜索任务
lobby/matchmaking/search-state=当前大厅的匹配搜索状态
lobby/members=当前大厅成员列表
lobby/members/:/grant-invite=!授予指定大厅成员邀请他人的权限
lobby/members/:/kick=!将指定玩家移出当前大厅
lobby/members/:/promote=!提升指定大厅成员或转让队长身份
lobby/members/:/revoke-invite=!撤销指定大厅成员邀请他人的权限
lobby/partyType=大厅的队伍公开类型，例如开放或关闭；枚举以当前版本为准
matchmaking/quick-search=!发起快速匹配搜索
notifications=大厅通知集合
notifications/:=指定大厅通知
parties/overrides/Enabled=!覆盖是否启用 Parties 组队系统的内部开关
parties/overrides/EnabledForTeamBuilderQueues=!覆盖 TeamBuilder 队列是否启用 Parties 的内部开关
party-active=当前是否存在激活的组队会话
party/eog-status=组队会话的赛后状态
party/:/join=!加入指定 ID 的队伍
play-again=!请求再次游戏或进入继续组队流程
play-again-decline=!拒绝再次游戏或继续组队邀请
received-invitations=当前玩家收到的大厅邀请
received-invitations/:/accept=!接受指定收到的大厅邀请
received-invitations/:/decline=!拒绝指定收到的大厅邀请
`);
moduleDef('lol-login','客户端登录与服务代理','登录会话、认证资料、后端服务代理和关闭锁。',`
access-token=!向登录模块提交访问令牌
account-state=当前账号的登录或初始化状态
change-summoner-name=!请求修改召唤师名称
login-data-packet=登录阶段返回的初始化数据包
login-in-game-creds=登录模块保存的游戏进程连接凭据
login-platform-credentials=登录模块保存的平台连接凭据
new-player-flow-completed=!报告新玩家初始化流程已完成
service-proxy-async-requests/:/:=指定服务及方法的异步代理请求
service-proxy-method-requests=!提交按方法名调用后端服务的代理请求
service-proxy-uuid-requests=!提交基于 UUID 的后端服务代理请求
session=当前客户端登录会话
session/invoke=!在登录会话中执行代理调用；允许方法由请求参数和客户端实现决定
shutdown-locks/:=指定名称的客户端关闭锁
summoner-created=!报告召唤师已创建
wallet=登录初始化数据中的钱包信息
`);
moduleDef('lol-loot','战利品与合成','战利品库存、配方、合成、兑换及新物品通知。',`
currency-configuration=战利品货币或材料配置
enabled=战利品功能是否启用
loot-grants=战利品发放记录
loot-grants/:=指定战利品发放记录
loot-items=战利品条目定义
new-player-check-done=新玩家战利品检查是否完成
new-player-check-done/:=!设置新玩家战利品检查完成标记
player-display-categories=战利品界面的玩家展示分类
player-loot=当前玩家的战利品列表
player-loot-map=按标识索引的玩家战利品映射
player-loot-notifications=玩家战利品通知
player-loot-notifications/:/acknowledge=!确认已看到指定战利品通知
player-loot/:=指定战利品的玩家持有信息
player-loot/:/context-menu=指定战利品的可用右键菜单操作；POST 的具体行为需查看参数
player-loot/:/new-notification=指定战利品的新增物品提示标记
player-loot/:/redeem=!请求兑换指定战利品
ready=战利品模块是否就绪
recipes/configuration=战利品合成配方配置
recipes/initial-item/:=以指定战利品为起始材料的合成配方或配方初始化数据
recipes/:/craft=!使用指定配方合成战利品，可能消耗材料
refresh=!请求刷新战利品库存和相关数据
`);
moduleDef('lol-loyalty','忠诚度权益','地区、渠道或其他忠诚度计划的状态通知，具体计划原文未说明。',`status-notification=忠诚度权益状态通知`);
moduleDef('lol-maps','游戏地图配置','地图、模式和变体对应的配置。',`map=!提交地图相关数据或选择请求；确切写入语义原文未说明
map/:=指定地图 ID 的配置
maps=可访问的地图配置列表
map/:/:=指定地图和游戏模式的配置
map/:/:/:=指定地图、模式及游戏变体的配置`);
moduleDef('lol-match-history','战绩与时间线','比赛详情、事件时间线、比赛列表及最近同场玩家。',`delta=战绩列表的增量更新数据
friend-matchlists/:=指定好友账号的比赛列表
game-timelines/:=指定比赛的事件时间线
games/:=指定比赛的详细战绩
matchlist=当前上下文下的比赛列表；查询对象和分页由参数决定
recently-played-summoners=最近同场游戏的玩家列表
web-url=战绩网页地址`);
moduleDef('lol-matchmaking','匹配与接受对局','搜索任务、匹配确认及搜索错误。',`ready-check=当前匹配确认状态
ready-check/accept=!接受当前匹配到的对局
ready-check/decline=!拒绝当前匹配到的对局
search=匹配搜索任务；GET 读状态，POST 发起，DELETE 取消，PUT 更新相关状态，具体载荷以 schema 为准
search/errors=匹配搜索错误集合
search/errors/:=指定匹配搜索错误`);
moduleDef('lol-missions','任务系统','任务定义、系列、玩家进度与参与偏好。',`data=任务系统基础数据
force=!请求强制触发任务数据处理或刷新；具体范围原文未说明
missions=当前可见的任务列表
player=玩家任务相关状态
player/opt=玩家任务参与或选择偏好
player/:=指定任务的玩家相关状态；不表示可任意修改服务端任务完成度
series=任务系列列表`);
moduleDef('lol-npe-rewards','新玩家奖励','等级奖励与登录奖励的展示状态。',`fetch-rewards=!请求加载新玩家奖励
level-rewards=新玩家等级奖励
level-rewards/state=新玩家等级奖励的状态
login-rewards=新玩家登录奖励
login-rewards/state=新玩家登录奖励的状态`);
moduleDef('lol-npe-tutorial-path','新玩家教程路径','教程列表、展示状态和新手奖励。',`how-to-win-stats=新手如何获胜相关的统计或引导内容
how-to-win-stats/mark-as-seen=!标记新手获胜统计或提示已被查看
rewards/champ=新手教程的英雄奖励信息
settings=新玩家教程路径设置
tutorials=新玩家教程列表及进度
tutorials/init=新玩家教程初始化状态
tutorials/:/view=指定教程的已查看状态`);
moduleDef('lol-patch','LOL 更新适配层','英雄联盟产品更新、文件检查、修复与重启。',`
environment=LOL 更新环境信息
notifications=LOL 更新通知集合
notifications/:=指定 LOL 更新通知
products/league_of_legends/detect-corruption-request=!请求检测英雄联盟产品文件损坏
products/league_of_legends/install-location=英雄联盟产品安装目录
products/league_of_legends/partial-repair-request=!请求执行英雄联盟产品的部分修复
products/league_of_legends/start-checking-request=!请求开始英雄联盟版本或文件检查
products/league_of_legends/start-patching-request=!请求开始英雄联盟补丁更新
products/league_of_legends/state=英雄联盟产品的更新状态
products/league_of_legends/stop-checking-request=!请求停止英雄联盟版本或文件检查
products/league_of_legends/stop-patching-request=!请求停止英雄联盟补丁更新
self-update-restart=!请求在客户端自更新后重启
`);
moduleDef('lol-perks','重铸符文','符文页、符文定义、符文系、当前选中页和自动修正通知。',`
currentpage=当前选中的重铸符文页
customizationlimits=符文自定义数量或配置限制
inventory=玩家重铸符文相关库存及权限
pages=玩家重铸符文页集合
pages/:=指定 ID 的重铸符文页
pages/:/auto-modified-selections=指定符文页的自动修改选择记录
perks=全部可见的符文定义
perks/ack-gameplay-updated=!确认已查看符文玩法更新
perks/disabled=当前禁用的符文定义
perks/gameplay-updated=存在玩法更新的符文信息
schema-version=符文数据结构版本
servicesettings=符文服务设置
settings=符文界面或个人设置
show-auto-modified-pages-notification=是否显示符文页被自动修改的通知标记
styles=符文系的定义及结构
update-page-order=!更新符文页的显示顺序
`);
moduleDef('lol-personalized-offers','个性化优惠','个人折扣商品列表、揭晓和购买流程。',`offers=当前玩家的个性化优惠列表
offers/purchase=!提交购买个性化优惠商品的请求，具体商品由请求体决定
offers/reveal=!请求揭晓个性化优惠商品，具体范围由请求体决定
offers/:/purchase=!请求购买指定个性化优惠商品
offers/:/reveal=!请求揭晓指定个性化优惠商品
status=个性化优惠功能状态
themed=个性化优惠主题数据`);
moduleDef('lol-pft','玩家反馈与调查','调查问卷及相关事件；PFT 缩写的正式含义原文未说明。',`events=!提交玩家调查或反馈相关事件
survey=玩家调查问卷及作答信息`);
moduleDef('lol-platform-config','平台配置','客户端平台配置命名空间与键值。',`initial-configuration-complete=平台初始配置是否加载完成
namespaces=平台配置命名空间列表
namespaces/:=指定命名空间的配置
namespaces/:/:=指定命名空间中指定键的配置`);
moduleDef('lol-player-behavior','玩家行为与处罚','封禁、聊天限制、排位限制、处罚说明及举报反馈。',`ban=玩家封禁状态或封禁信息
chat-restriction=玩家聊天限制状态
config=玩家行为系统配置
ranked-restriction=玩家排位限制状态
reform-card=玩家处罚或行为改正说明卡片
reporter-feedback=玩家举报后的处理反馈集合
reporter-feedback/:=指定举报反馈通知记录`);
moduleDef('lol-player-level-up','玩家升级','玩家升级信息和插件级升级通知。',`level-up=玩家等级提升信息
level-up-notifications/:=指定插件的玩家升级通知`);
moduleDef('lol-player-messaging','玩家提示消息','一般提示及庆祝类通知。',`celebration/notification=庆祝类玩家通知
celebration/notification/:/acknowledge=!确认并移除指定庆祝类玩家通知
notification=一般玩家通知
notification/:/acknowledge=!确认并移除指定一般玩家通知`);
moduleDef('lol-player-preferences','玩家偏好服务','偏好数据、偏好后端与哈希计算。',`hash=!计算或提交玩家偏好数据的哈希，具体算法原文未说明
player-preferences-endpoint-override=!覆盖玩家偏好服务使用的后端端点
player-preferences-ready=玩家偏好服务是否就绪
preference=玩家偏好数据；类型由请求体决定
preference/:=指定类型的玩家偏好数据`);
moduleDef('lol-pre-end-of-game','赛后前置流程','赛后结算前有顺序与优先级的内部事件协调。',`complete/:=!报告指定赛后前置序列事件已完成
currentSequenceEvent=当前正在处理的赛后前置序列事件
registration/:=指定赛后前置序列事件的注册记录
registration/:/:=!按给定优先级注册指定赛后前置序列事件`);
moduleDef('lol-premade-voice','组队语音','玩家层组队语音、麦克风测试、成员音量及个人输入配置。',`
availability=组队语音功能可用性
capturedevices=组队语音的音频输入设备及所选设备
first-experience=组队语音首次体验或引导状态
first-experience/game=!记录或触发游戏内的组队语音首次体验
first-experience/lcu=!记录或触发客户端内的组队语音首次体验
first-experience/reset=!重置组队语音首次体验标记
gameClientUpdatedPTTKey=!通知语音模块游戏进程已更新按键说话快捷键
mic-test=麦克风测试会话；POST 启动，DELETE 结束，GET 读取状态
participant-records=组队语音参与者的记录
participants=当前组队语音参与者列表
participants/:/mute=指定语音参与者的静音状态
participants/:/volume=指定语音参与者的播放音量
push-to-talk/check-available=!检查组队语音按键说话是否可用
self/activationSensitivity=本地玩家语音自动激活灵敏度
self/inputMode=本地玩家语音输入模式，例如按键说话或语音激活
self/micLevel=本地玩家麦克风输入电平设置
self/mute=本地玩家麦克风静音状态
session=本地玩家的组队语音会话
settings=组队语音偏好设置
settings/reset=!重置组队语音偏好设置
`);
moduleDef('lol-purchase-widget','购买组件','统一购买组件的配置、商品校验与购买。',`configuration=购买组件配置
purchaseItems=!提交商品购买请求
validateItems=!校验待购买商品及购买条件，不等同于完成购买`);
moduleDef('lol-queue-eligibility','队列准入资格','玩家或队伍是否满足队列条件。',`eligibility=队列准入资格结果；POST 根据请求载荷计算资格
initial-configuration-complete=队列准入配置是否初始化完成
custom=自定义模式的准入资格`);
moduleDef('lol-ranked-stats','旧排位统计','玩家排位汇总及统计服务状态。',`stats/:=指定召唤师的排位统计
status=排位统计服务状态`);
moduleDef('lol-ranked','排位资料','段位、排位概览、榜单、胜点变动及奖励。',`apex-leagues/:/:=指定队列和顶端段位的联盟或排行榜数据
current-lp-change-notification=当前胜点变化通知
notifications=排位通知列表
notifications/:/acknowledge=!确认已查看指定排位通知
ranked-dashboard/:=指定召唤师的排位仪表盘数据
ranked-overview/:=指定召唤师的排位概要
ranked-reward-update=排位奖励更新信息
ranked-rewards-data=排位奖励资料
signed-ranked-dashboard=当前玩家带签名的排位仪表盘数据
summoner-top-champs/:=指定召唤师的排位常用或表现突出英雄
vignette-notifications=排位 vignette 展示类通知；具体触发条件原文未说明
tiers=排位段位层级定义`);
moduleDef('lol-recommendations','推荐内容','客户端推荐结果；不是专门的出装推荐接口。',`recommendations=客户端推荐内容；推荐对象和字段原文未说明`);
moduleDef('lol-regalia','玩家排位装饰','玩家展示用的段位或身份装饰。',`config=玩家排位装饰配置
current-summoner/regalia=当前玩家的排位或身份展示装饰
summoners/:/regalia=指定召唤师的排位或身份展示装饰
summoners/:/regalia/async=指定召唤师的异步装饰查询结果或任务信息`);
moduleDef('lol-replays','回放','ROFL 文件、下载、观看及回放元数据。',`configuration=回放功能配置
metadata/:=指定比赛的回放元数据及可用状态
metadata/:/create/gameVersion/:/gameType/:/queueId/:=!根据比赛 ID、游戏版本、类型和队列创建回放元数据
rofls/path=当前回放文件保存目录
rofls/path/default=默认回放文件保存目录
rofls/scan=!扫描本地回放文件并更新索引
rofls/:/download=!请求下载指定比赛的回放
rofls/:/download/graceful=!以 graceful 方式请求下载指定比赛回放；与普通下载的精确差异原文未说明
rofls/:/watch=!请求启动客户端观看指定比赛回放
metadata/:/create=!根据请求载荷创建指定比赛的回放元数据
`);
moduleDef('lol-service-status','服务状态公告','LCU 状态与滚动服务公告。',`lcu-status=客户端服务健康或可用状态
ticker-messages=服务公告或状态滚动消息`);
moduleDef('lol-settings','账号与本地设置','按类别、偏好类型组织的账号设置与本机设置。',`account/didreset=账号设置是否已重置
account/save=!保存当前账号设置
account/:=指定类别的账号设置
local/:=指定类别的本地设置
account/:/:=指定偏好类型与类别的账号设置
didreset/:=指定偏好类型的设置是否已重置
ready=设置模块是否就绪`);
moduleDef('lol-shutdown','关闭通知','LOL 客户端关闭相关提示。',`notification=客户端关闭通知`);
moduleDef('lol-simple-dialog-messages','简单对话框消息','客户端简单弹窗消息队列。',`messages=简单对话框消息集合
messages/:=指定简单对话框消息`);
moduleDef('lol-spectator','观战','好友观战、观战信息与启动。',`buddy/spectate=!发起观战好友的请求
spectate=当前观战信息或状态
spectate/launch=!请求启动观战`);
moduleDef('lol-store','商城','商品目录、商城入口、赠礼好友、订单通知与购买。',`catalog=商城商品目录；可按库存类别或商品 ID 筛选
getStoreUrl=商城页面 URL
giftablefriends=可接收赠礼的好友列表
lastPage=商城上次访问页面记录
login=商城登录或会话相关信息；是否触发初始化需看当前实现
notifications/acknowledge=!确认已处理商城通知
order-notifications=商城订单通知集合
order-notifications/:=指定商城订单通知
paymentDetails=商城支付相关信息
skins/:=指定商城皮肤的商品信息
skins/:/purchase=!提交购买指定皮肤的请求
status=商城服务状态
wallet=商城钱包余额或钱包信息
:=指定商城页面类型对应的数据或页面入口`);
moduleDef('lol-suggested-players','推荐组队玩家','推荐同伴、排除记录及胜利队友反馈。',`reported-player=!向推荐服务报告某玩家；不一定等同于违规举报
suggested-players=推荐组队玩家列表
suggested-players/:=指定玩家的组队推荐记录
victorious-comrade=!向推荐服务提交胜利队友相关反馈或记录`);
moduleDef('lol-summoner','召唤师资料','玩家身份、头像、名称、等级经验及基础资料查询。',`check-name-availability/:=指定召唤师名称是否可用
current-summoner=当前已登录召唤师的基础资料
current-summoner/autofill=当前玩家的补位相关状态
current-summoner/icon=当前玩家的召唤师头像
current-summoner/jwt=当前召唤师身份相关的 JWT 令牌
current-summoner/name=当前玩家的召唤师名称
current-summoner/rerollPoints=当前玩家的重随机点数
current-summoner/summoner-profile=当前玩家的召唤师扩展资料
current-summoner/xpInfo=!提交或刷新当前玩家经验值相关资料；精确用途原文未说明
summoner-profile=召唤师扩展资料，查询上下文由参数或当前会话决定
summoners=召唤师资料查询集合；GET 与 POST 的查询输入方式可能不同，需看参数
summoners/:=指定 ID 的召唤师基础资料
summoner-icons=一组召唤师头像查询结果
summoner-names=一组召唤师名称查询结果`);
moduleDef('lol-tencent-qt','腾讯 QT 集成状态','腾讯地区特定 UI 功能状态。',`ui-states=腾讯 QT 集成的全部 UI 状态
ui-states/:=腾讯 QT 集成中指定功能的 UI 状态`);
moduleDef('lol-token-upsell','代币促销','代币相关的促销展示数据。',`all=全部可用的代币促销数据`);
moduleDef('lol-trophies','奖杯展示','当前玩家或指定玩家的奖杯资料。',`current-summoner/trophies/profile=当前玩家的奖杯展示资料
players/:/trophies/profile=指定玩家的奖杯展示资料`);
moduleDef('network-testing','网络测试','网络实验、游戏延迟和结果清理。',`experiments=网络测试实验配置或列表
game-latency=游戏网络延迟测试状态或结果
game-latency/results=游戏网络延迟测试结果记录`);
moduleDef('patcher','通用更新器','产品安装、补丁、P2P、文件修复和更新器状态。',`
executable-version=更新器可执行文件版本
notifications=更新器通知集合
notifications/:=指定更新器通知
p2p/status=P2P 更新传输状态或设置
products=更新器管理的产品列表
products/league_of_legends/full-repair-request=!请求对英雄联盟产品执行完整修复
products/:=指定产品的注册或更新配置
products/:/detect-corruption-request=!请求检测指定产品的文件损坏
products/:/inject-error=!向指定产品更新流程注入测试错误，属于诊断或测试用途
products/:/partial-repair-request=!请求对指定产品执行部分修复
products/:/paths=指定产品的安装及资源路径
products/:/signal-start-patching-delayed=!通知更新器指定产品的补丁启动被延迟
products/:/start-checking-request=!请求开始检查指定产品的版本或文件
products/:/start-patching-request=!请求开始更新指定产品
products/:/state=指定产品的更新状态
products/:/stop-checking-request=!请求停止指定产品的版本或文件检查
products/:/stop-patching-request=!请求停止指定产品的补丁更新
products/:/tags=指定产品的更新标签
products/:/:/http-headers=指定产品组件的 HTTP 请求头配置
self-update-restart=!请求在自更新后重启更新器或客户端
status=通用更新器状态
`);
moduleDef('payments','支付入口','支付管理组件启动地址。',`pmc-start-url=!请求获取或生成支付管理组件的启动地址`);
moduleDef('performance','性能诊断','进程内存、CEF 进程性能、CPU 计时及系统信息。',`memory=进程内存状态
process/:=!将指定进程注册到性能信息采集中
report=CEF 进程的性能统计报告
report/restart=!重置 CPU 性能计时并返回性能统计结果
system-info=当前机器的硬件和软件规格`);
moduleDef('player-notifications','通用玩家通知','通知配置及通知记录的创建、读取、更新和删除。',`config=通用玩家通知配置
notifications=通用玩家通知集合
notifications/:=指定通用玩家通知`);
moduleDef('plugin-manager','插件管理与诊断','内置插件清单、说明、状态与诊断数据。',`plugin-stats/log=!将当前插件统计写入日志
status=插件管理器状态
descriptions=全部插件说明
descriptions/:=指定插件的说明
plugins=全部插件的诊断信息
plugins/:=指定插件的诊断信息
plugins-manifest=插件清单`);
moduleDef('process-control','进程生命周期控制','客户端退出、重启以及进入修复或更新流程。',`process=进程控制模块信息
process/quit=!请求退出应用
process/restart=!请求重启应用；已有延迟重启等待时不重复安排，可选指定版本
process/restart-to-repair=!请求重启应用以执行完整修复，包括自身修复
process/restart-to-update=!请求重启应用以进行自身更新`);
moduleDef('recofriender','社交好友推荐','外部社交网络注册、联系人同步、推荐和忽略记录。',`config=好友推荐服务配置
config/:=指定社交网络的好友推荐配置
contacts=好友推荐联系人集合
contacts/:/available=!标记指定账号联系人可用于推荐
contacts/:/dismissed=!标记已忽略指定账号联系人
contacts/:/invited=!标记已邀请指定账号联系人
debug=好友推荐调试配置或状态
faq-url=好友推荐功能的帮助页面地址
registrations=已注册的外部社交网络关联列表
registrations/:=指定社交网络的注册或关联记录
contacts/page=分页的好友推荐联系人
contacts/:=指定账号的推荐联系人资料
dismissed=已忽略的好友推荐记录集合
dismissed/page=分页的已忽略好友推荐记录`);
moduleDef('riot-messaging-service','Riot 消息服务','RMS 连接、权益凭据与会话状态。',`connect=Riot 消息服务连接
entitlements=Riot 消息服务使用的权益认证数据
reconnect=!请求重新连接 Riot 消息服务
session=Riot 消息服务会话
state=Riot 消息服务状态`);
moduleDef('riotclient','客户端外壳与窗口控制','客户端进程参数、窗口、地区语言、认证令牌、诊断和缩放。',`
addorupdatemetric=!添加或更新客户端度量指标
affinity=应用运行时 affinity 属性；原文未限定为 CPU 亲和性，不应直接等同
app-name=不带扩展名的应用名称
app-port=本地远程调用服务监听的 TCP 端口
auth-token=本地远程调用服务使用的认证令牌
command-line-args=应用启动命令行参数
get_region_locale=当前地区与语言
kill-and-restart-ux=!终止并重新启动客户端 UX 进程，原文说明用于 UX 崩溃后的恢复
kill-ux=!终止客户端 UX 进程
launch-ux=!启动客户端 UX 进程
machine-id=以 Base64 编码的机器 UUID
new-args=!向应用传入新的参数数据
region-locale=当前地区与语言设置
region-locale/ack=!由 UX 确认地区与语言设置更新
set_region_locale=!更新客户端地区与语言
show-swagger=!使用系统默认浏览器打开 Swagger 文档
splash=客户端启动画面
system-info/basic-info=基础系统信息，包括操作系统、内存、处理器速度与物理核心数
trace=已完成的客户端调度器追踪结果
unload=!卸载客户端 UX 进程
ux-crash-count=UX 是否崩溃的信息；名称包含 count，但原文描述未保证响应一定是计数
ux-flash=!使 UX 主窗口及任务栏或 Dock 图标闪烁
ux-load-complete=!通知客户端 UX 主窗口已加载完成
ux-minimize=!最小化 UX 及其窗口，不终止 UX
ux-show=!显示现有 UX，若不存在则创建并显示
ux-state=当前 UX 状态
ux-state/ack=!由 UX 确认其状态更新
auth-tokens/:=本地远程调用服务的指定认证令牌注册记录
bugsplat/logs=!将请求附带的日志加入 BugSplat 崩溃报告
bugsplat/platform-id=BugSplat 的平台标识，便于筛选崩溃报告
zoom-scale=客户端 UI 缩放比例
`);
moduleDef('rso-auth','Riot 单点登录认证','RSO 认证会话、令牌、用户资料和认证配置。',`authorization=当前 RSO 授权会话
authorization/access-token=RSO 访问令牌
authorization/entitlements-token=RSO 权益令牌
authorization/error=RSO 授权错误
authorization/gas=!提交 GAS 认证相关请求；GAS 的具体协议原文未说明
authorization/gas-token=GAS 认证令牌
authorization/id-token=RSO 身份令牌
authorization/refresh=!刷新 RSO 授权或令牌
authorization/userinfo=RSO 授权用户资料
config=RSO 认证配置
config/:=指定平台的 RSO 认证配置
device-id=!请求生成或提交 RSO 设备标识；具体行为原文未说明
status/:=指定平台的 RSO 认证状态`);
moduleDef('sanitizer','文本净化','文本净化以及是否包含被处理内容的检查。',`containsSanitized=!检查文本是否包含需净化或已净化的内容；精确判定以实现为准
sanitize=!对输入文本执行净化或过滤
status=文本净化服务状态`);
moduleDef('swagger','接口自描述文档','客户端自身暴露的 API 文档入口。',`api-docs=Swagger v1 API 资源索引
api-docs/:=指定 API 的 Swagger v1 接口声明
swagger.json=Swagger v2 格式的接口文档
openapi.json=OpenAPI v3 格式的接口文档`);
moduleDef('system','构建信息','构成当前构建的组件及版本。',`builds=当前构建使用的组件或构建产物信息`);
moduleDef('telemetry','遥测事件','启动时间、遥测公共字段及分析事件提交。',`application-start-time=应用启动时的 UNIX 毫秒时间戳
common-data/:=附加到后续遥测事件的指定公共字段
events-with-perf-info/:=!提交指定类型的遥测事件，并附带当前性能计数后重置相关计数
events/:=!提交指定类型的普通遥测事件`);
moduleDef('tracing','运行追踪','记录事件、模块说明与阶段的开始和结束。',`trace/event=!记录一条运行追踪事件
trace/module=!记录追踪模块说明
trace/phase/begin=!记录某个运行追踪阶段开始
trace/phase/end=!记录某个运行追踪阶段结束`);
moduleDef('{plugin}','插件资源文件','下载插件后端提供的资源文件或读取其响应头。',`assets/:=指定插件的指定后端资源文件`);

const builtin={
AsyncDelete:'取消异步任务，或移除该任务已完成状态的记录。',AsyncResult:'读取已完成异步任务的结果。',AsyncStatus:'读取异步任务当前的详细执行状态。',Cancel:'尝试取消指定异步任务。',Exit:'关闭当前调用连接；原文描述不是“退出整个客户端”。',Help:'读取可用函数及数据类型的信息。',LoggingGetEntries:'读取自上次调用后缓冲的日志条目。',LoggingMetrics:'读取全部日志度量指标。',LoggingMetricsMetadata:'读取全部日志度量指标的元数据。',LoggingStart:'初始化并启动日志系统。',LoggingStop:'结束日志系统。',MemoryFilterEnable:'启用或停止内存筛选日志：符合筛选条件的分配与释放会输出日志。',MemoryFilterSet:'设置内存日志的大小、地址等筛选条件；使用 MemoryFilterEnable 启停输出。',MemoryPools:'读取当前内存池使用情况。',MemoryStats:'读取聚合内存使用信息；原文指出 allocation_count 已弃用，应使用 allocated_count。',MemoryUsage:'按调用栈位置读取内存占用。',Subscribe:'订阅指定名称的事件，可指定事件输出格式。',Unsubscribe:'取消订阅指定名称的事件。',WebSocketFormat:'设置 WebSocket 控制台的输出格式。'
};
const special={
'DELETE /async/v1/status/{asyncToken}':'取消指定异步任务，或移除该任务已完成状态的记录。',
'GET /async/v1/status/{asyncToken}':'读取指定异步任务的详细状态。',
'GET /async/v1/result/{asyncToken}':'读取指定已完成异步任务的结果。',
'POST /cookie-jar/v1/cookies':'设置一个客户端 Cookie。',
'DELETE /riotclient/splash':'隐藏客户端启动画面。',
'PUT /riotclient/splash':'显示客户端启动画面。',
'GET /riotclient/region-locale':'读取当前地区和语言设置。',
'PUT /riotclient/v1/auth-tokens/{authToken}':'将指定字母数字令牌注册为本地远程调用服务的认证密码，配合用户名 riot 使用。',
'DELETE /riotclient/v1/auth-tokens/{authToken}':'注销指定的本地远程调用认证令牌。',
'DELETE /riotclient/affinity':'清除应用当前的运行时 affinity 配置；原文未说明其具体结构。',
'POST /riotclient/affinity':'设置应用当前的运行时 affinity 配置；原文未说明其具体结构。',
'GET /riotclient/affinity':'读取应用当前的运行时 affinity 配置；原文未说明其具体结构。',
'HEAD /{plugin}/assets/{path}':'仅请求指定插件后端资源的响应头，不下载资源正文。',
'GET /{plugin}/assets/{path}':'下载指定插件后端提供的资源文件。',
'GET /lol-champ-select/v1/session':'读取当前英雄选择会话，用于了解选禁行动、双方阵容和选人阶段数据。',
'GET /lol-gameflow/v1/gameflow-phase':'读取当前游戏流程阶段，用于判断处于大厅、匹配确认、选人、游戏中或赛后等阶段。',
'POST /lol-gameflow/v1/pre-end-game-transition':'触发游戏结束前的内部过渡处理；确切状态转换原文未说明。',
'POST /lol-item-sets/v1/item-sets/{summonerId}/sets':'为指定玩家提交或新增自定义装备方案；合并、覆盖规则原文未说明。',
'PUT /lol-item-sets/v1/item-sets/{summonerId}/sets':'写入指定玩家的自定义装备方案集合；替换粒度以请求 schema 为准。',
'POST /lol-perks/v1/pages':'创建一张重铸符文页；可写字段和符文页容量限制需查当前客户端。',
'DELETE /lol-perks/v1/pages':'删除符文页集合或对集合执行删除；删除范围原文未给出，不能假定只删除当前页。',
'DELETE /lol-perks/v1/pages/{id}':'删除指定 ID 的重铸符文页。',
'PUT /lol-perks/v1/currentpage':'设置当前使用的重铸符文页。',
'POST /lol-chat/v1/conversations/{id}/messages':'向指定聊天会话发送消息。',
'POST /lol-chat/v1/friend-requests':'提交好友申请；目标标识格式由当前请求 schema 决定。',
'POST /lol-chat/v1/blocked-players':'向聊天黑名单添加玩家。',
'POST /lol-chat/v1/conversations/{id}/closed':'提交指定会话的关闭标记或关闭操作；与 PUT 的区别原文未说明。',
'PUT /lol-chat/v1/conversations/{id}/closed':'更新指定会话的关闭标记；与 POST 的区别原文未说明。',
'POST /lol-lobby/v2/lobby/matchmaking/search':'为当前大厅发起匹配搜索。',
'DELETE /lol-lobby/v2/lobby/matchmaking/search':'取消当前大厅的匹配搜索。',
'POST /lol-lobby/v2/lobby':'创建或切换游戏大厅，队列与房间配置由请求体指定。',
'DELETE /lol-lobby/v2/lobby':'退出或删除当前本地大厅会话，实际影响受队长身份及客户端状态约束。',
'POST /lol-matchmaking/v1/search':'提交匹配搜索请求；前置大厅与资格条件以客户端为准。',
'DELETE /lol-matchmaking/v1/search':'取消匹配搜索任务。',
'GET /lol-matchmaking/v1/search':'读取当前匹配搜索状态。',
'PUT /lol-matchmaking/v1/search':'更新匹配搜索相关数据；原文未说明 PUT 与 POST 的具体差异。',
'DELETE /lol-chat/v1/conversations/{id}/messages':'清理指定会话的消息记录；影响范围及是否只清本地记录原文未说明。',
'DELETE /lol-highlights/v1/highlights/{id}':'删除指定精彩片段记录；是否同时删除视频文件原文未说明。',
'GET /lol-login/v1/login-in-game-creds':'读取游戏进程连接凭据；具体字段和有效阶段原文未说明。',
'GET /lol-store/v1/login':'请求商城登录相关信息或初始化入口；这是 GET 路由，但是否有会话副作用原文未说明。'
};

const parameterNames={asyncToken:'异步任务标识',id:'此路径下资源的 ID，具体对象看路径上下文',summonerId:'召唤师 ID；不应与 accountId 或 PUUID 混用',accountId:'账号 ID；不应直接当作召唤师 ID',puuid:'玩家全局唯一标识',championId:'英雄 ID',championSkinId:'英雄皮肤 ID',skinId:'皮肤 ID',wardSkinId:'守卫皮肤 ID',summonerIconId:'召唤师头像 ID',gameId:'对局 ID',queueId:'游戏队列 ID',queue_id:'游戏队列 ID',queue:'队列筛选值，具体类型以参数定义为准',queueType:'队列类型',gameVersion:'游戏版本',gameType:'游戏类型',gameMode:'游戏模式',gameMutator:'游戏变体标识',mapId:'地图 ID',gameTypeConfigId:'游戏类型配置 ID',season:'赛季标识',position:'分路或位置',tier:'段位层级',inventoryType:'库存或物品类别',currencyType:'货币类别',eventName:'订阅或取消订阅的事件名称',eventType:'事件类型',format:'输出格式',path:'资源路径或设置键路径',plugin:'插件名称',pluginName:'插件名称',pageId:'符文页 ID',loadoutId:'综合配置方案 ID',scope:'配置方案的作用域',scopeItemId:'作用域对象标识',rosterId:'Clash 战队名单 ID',tournamentId:'赛事 ID',bracketId:'淘汰赛分组 ID',uuid:'通用唯一标识',clubKey:'俱乐部标识',lootId:'战利品 ID',lootName:'战利品名称或键',recipeName:'合成配方名称',network:'外部社交网络标识',ns:'配置命名空间',key:'键名',category:'设置类别',ppType:'玩家偏好类型',platformId:'平台或地区标识',sessionId:'会话 ID',participantId:'语音参与者 ID',partyId:'队伍 ID',invitationId:'邀请 ID',notificationId:'通知 ID',messageId:'消息 ID',highlightId:'精彩片段 ID',delaySeconds:'延迟秒数',priority:'处理优先级',sequenceEventName:'赛后前置序列事件名称',serviceName:'后端代理服务名',methodName:'后端代理方法名',lockName:'关闭锁名称',authToken:'认证令牌',processId:'进程 ID',name:'待检查的名称',feature:'功能标识',newValue:'要写入的新值',pageType:'商城页面类型','product-id':'更新器产品 ID','component-id':'更新器产品组件 ID',summonerInternalName:'电脑玩家内部名称',hovercard:'是否使用悬浮信息卡展示形式',begIndex:'起始索引，边界含义需看当前实现',endIndex:'结束索引，边界含义需看当前实现',beginIndex:'起始索引，边界含义需看当前实现',body:'请求正文，结构以当前客户端 schema 为准',api:'要读取接口声明的 API 标识',contextName:'上下文名称',minSize:'最小内存大小筛选值',maxSize:'最大内存大小筛选值',minCount:'最少分配次数筛选值',minAddress:'最小地址筛选值',maxAddress:'最大地址筛选值'};
Object.assign(parameterNames,{
name:'名称字符串，所指对象由路径及操作决定',JWT:'JSON Web Token 认证数据',target:'目标对象或目标范围',buffered:'是否使用缓冲模式',severity:'日志严重级别',force:'是否强制执行相关请求',doAsync:'是否以异步方式执行',summonerIds:'召唤师 ID 集合',summonerNames:'召唤师名称集合',limit:'结果数量上限',sortRule:'排序规则',matchId:'比赛或匹配标识，具体对象以模块为准',summonerName:'召唤师名称',message:'消息内容',enabled:'是否启用',ip_address:'IP 地址',inventoryTypes:'库存类别集合',currencyTypes:'货币类别集合',team:'队伍标识或队伍数据',enabledForTeambuilderQueues:'是否为 TeamBuilder 队列启用',pluginId:'插件标识',responseMethodName:'代理调用响应方法名',responseErrorName:'代理调用错误响应名',payload:'请求载荷，具体结构原文未给出',destination:'代理调用或消息的目标',method:'代理调用的方法名',repeat:'是否重复或重复参数，精确语义需查当前实现',missionId:'任务 ID',tutorialId:'教程 ID',restart:'是否重启',forceRestartOnSelfUpdate:'自更新时是否强制重启',type:'类型标识，取值以模块参数定义为准',hash:'哈希值',teamSize:'队伍人数',pickMode:'选人模式',queueIds:'游戏队列 ID 集合',queueTypes:'游戏队列类型集合',numberOfRecommendations:'希望获取的推荐条数',itemId:'物品 ID 或物品 ID 集合，取决于类型定义',action:'操作标识',giftRecipientAccountId:'赠礼接收方的账号 ID',giftMessage:'赠礼留言',ids:'资源 ID 集合',enable:'是否开启',error:'错误信息或测试错误配置',sampleLength:'采样时长或采样长度；单位原文未说明',sampleCount:'采样数量',full:'是否读取完整信息',restartVersion:'重启后指定使用的版本',selfUpdateUrl:'自更新资源地址',source:'来源标识',friendState:'好友关系或在线状态筛选值',retainInCache:'是否保留在缓存中',cb:'回调标识或回调参数；原文未说明具体格式',start:'开始位置或起始标记，具体含义由操作决定',group:'分组标识',object:'提交的对象数据',value:'写入的值',newAffinity:'新的应用运行时 affinity 配置，结构原文未说明',region:'地区标识',locale:'语言区域标识',newZoomScale:'新的客户端 UI 缩放比例','if-none-match':'用于条件请求的实体标签，与资源 ETag 比较'
});
Object.assign(special,{
'POST /lol-summoner/v1/summoners':'提交召唤师资料查询或批量加载请求；请求体与批量语义原文未说明，不应理解为创建账号。',
'GET /lol-summoner/v1/summoners':'按 name 查询召唤师资料，原文将 name 列为必填查询参数。',
'POST /lol-queue-eligibility/v1/eligibility':'请求计算或检查队列准入资格，输入结构及允许条件由客户端决定。',
'POST /lol-queue-eligibility/v2/eligibility':'使用 v2 路由请求计算队列准入资格；与 v1 的字段差异原文未说明。',
'POST /rso-auth/v1/authorization/userinfo':'提交 RSO 用户信息相关请求；这是查询触发还是更新原文未说明。',
'GET /lol-chat/v1/conversations/{id}/messages':'读取指定聊天会话的消息记录。',
'POST /data-store/v1/install-settings/{path}':'设置指定键对应的安装级设置数据。',
'POST /lol-summoner/v1/current-summoner/name':'请求设置或修改当前召唤师名称；可用条件与改名规则由客户端及服务端决定。',
'GET /lol-matchmaking/v1/search':'读取当前匹配搜索状态。'
});
function getGroupAndKey(route){const pieces=route.split('/').filter(Boolean);let module=pieces.shift();let suffix=pieces.filter(x=>!/^v\d+$/.test(x)).join('/').replace(/\{[^}]+\}/g,':');if(module==='lol-champ-select-legacy')return {module,base:'lol-champ-select',suffix};if(module==='gcloud-voice-chat')return {module,base:'voice-chat',suffix};if(module==='lol-lobby-team-builder'&&suffix.startsWith('champ-select/'))return {module,base:'lol-champ-select',suffix:suffix.slice(13)};return{module,base:module,suffix}}
function classify(op){const {module,base,suffix}=getGroupAndKey(op.path);const def=groups[base];const moduleInfo=groups[module]||def;const label=module==='lol-champ-select-legacy'?'旧版英雄选择':module==='gcloud-voice-chat'?'GCloud 底层语音服务':builtin[module]?'内置命令与诊断':moduleInfo?.label||module;return{module,base,suffix,label,about:builtin[module]?'客户端内置的异步、日志、内存及事件命令。':moduleInfo?.about||'',def}}
const missing=[];
function explain(op,info){if(special[op.method+' '+op.path])return special[op.method+' '+op.path];if(builtin[info.module])return builtin[info.module];const subject=info.def?.rules[info.suffix];if(!subject){missing.push(op.method+' '+op.path+' => '+info.base+' / '+info.suffix);return '原文没有说明；仅能确认属于'+info.label+'模块。该路径的精确用途待当前客户端 schema 或实现核实。'}if(subject.startsWith('!'))return subject.slice(1)+'。';let action={GET:'读取',HEAD:'仅读取响应头：',POST:'提交处理请求：',PUT:'写入或更新',PATCH:'局部修改',DELETE:'删除、移除或终止'}[op.method];return action+subject+'。'}
const opRegex=/<div id="(operation-[^"]+)"([\s\S]*?)(?=<div id="operation-|<h2 id="swagger--[^"]+"|<script|$)/g;
const ops=[];
for(const m of source.matchAll(opRegex)){
 const block=m[2];const head=block.match(/<span class="operation-name">(\w+)<\/span>\s*<strong>([^<]+)<\/strong>/);if(!head)throw Error('missing head '+m[1]);
 const op={method:head[1],path:clean(head[2]),anchor:m[1]};const info=classify(op);op.module=info.module;op.moduleName=info.label;op.moduleAbout=info.about;
 const originalSummary=clean(block.match(/<div class="operation-summary">([\s\S]*?)<\/div>/)?.[1]);const originalDescription=clean(block.match(/<section class="sw-operation-description">([\s\S]*?)<\/section>/)?.[1]);
 op.evidence=originalSummary||originalDescription?'原文有说明':'路径推断';op.explanation=explain(op,info);
 op.parameters=[];const params=block.match(/<section class="sw-request-params">([\s\S]*?)<\/section>/)?.[1]||'';
 for(const row of params.matchAll(/<tr>([\s\S]*?)<\/tr>/g)){const cells=[...row[1].matchAll(/<td[^>]*>([\s\S]*?)<\/td>/g)].map(x=>x[1]);if(cells.length<4)continue;const name=clean(cells[0]),location=clean(cells[2]),type=clean(cells[3]);const param={name,location,type:type||'原文未给出',required:location==='path'||/json-property-required/.test(cells[4]||''),note:parameterNames[name]||(location==='body'?'请求正文或正文中的对象；完整结构原文未给出':'该参数的精确含义原文未说明，需结合路径和当前 schema')};if(!op.parameters.some(p=>p.name===name&&p.location===location))op.parameters.push(param);}
 const placeholders=[...op.path.matchAll(/\{([^}]+)\}/g)].map(x=>x[1]);for(const name of placeholders)if(!op.parameters.some(p=>p.name===name&&p.location==='path'))op.parameters.push({name,location:'path',type:'原文未列类型',required:true,note:parameterNames[name]||'此路径下资源的标识或筛选值，确切含义需看当前 schema'});
 op.responses=[...new Set([...block.matchAll(/<dt class="sw-response-[^"]+">([\s\S]*?)<\/dt>/g)].map(x=>clean(x[1])))];
 op.source='https://lcu.vivide.re/#'+op.anchor;
 op.effect=op.method==='GET'||op.method==='HEAD'?'读取型方法（不据此保证无副作用）':op.method+' 提交方法（行为见用途）';
 if(/purchase|craft|redeem|set-ticket|ticket-offer/.test(op.path))op.effect='交易或消耗相关';
 else if(op.module!=='lol-token-upsell'&&/(?:auth|creds|credentials|token|cookie|wallet|payment)/i.test(op.path.replace(/\{[^}]+\}/g,'')))op.effect='凭据／账号／钱包相关';
 else if(/repair|corruption|inject-error|process\/quit|process\/restart|kill-|unload|shutdown|self-update-restart/.test(op.path))op.effect='进程／更新／修复相关';
 ops.push(op);
}
if(missing.length){fs.writeFileSync(path.join(__dirname,'lcu-unmapped.txt'),missing.join('\n'));console.log('UNMAPPED',missing.length);console.log(missing.join('\n'));}
const unique=new Set(ops.map(x=>x.method+' '+x.path));if(unique.size!==ops.length)throw Error('duplicate operations');
const prefixes=[...new Set(ops.map(x=>x.module))];const grouped=Object.groupBy(ops,x=>x.module);const evidenceCount=ops.filter(x=>x.evidence==='原文有说明').length;
const meta={source:'https://lcu.vivide.re/',sourceClientVersion:'8.24',retrievedDate:'2026-10-19',operations:ops.length,uniquePaths:new Set(ops.map(x=>x.path)).size,pathPrefixes:prefixes.length,describedInSource:evidenceCount,inferred:ops.length-evidenceCount,scope:'该站点页面列出的全部 HTTP 方法与路径组合；不包含其他客户端版本新增接口，也未实际调用本机游戏客户端。',explanationPolicy:'原文有说明的条目结合原文解释；其余按路径和方法推断，不将推断视为已验证行为。字段类型和状态码按原文保存，重复参数去重。请求正文没有完整结构时不编造 JSON。'};
const listedVersion=clean(source.match(/class="sw-info-version">([^<]+)</)?.[1]);if(listedVersion!==meta.sourceClientVersion)throw Error('source version mismatch');
const summarySet=new Set([...source.matchAll(/<td><a href="#operation-[^"]+">(GET|POST|PUT|PATCH|DELETE|HEAD) ([^<]+)<\/a><\/td>/g)].map(x=>x[1]+' '+clean(x[2])));
const summaryOnly=[...summarySet].filter(x=>!unique.has(x));if(summaryOnly.length)throw Error('summary endpoints missing from output: '+summaryOnly.join(', '));
meta.coverage={summaryOperations:summarySet.size,detailOperations:ops.length,extraOperationsInDetails:ops.filter(x=>!summarySet.has(x.method+' '+x.path)).map(x=>x.method+' '+x.path),unmappedOperations:missing.length,sourceSha256:require('node:crypto').createHash('sha256').update(source).digest('hex')};
if(missing.length)throw Error('Every endpoint must have a specific explanation');
const out=path.join(root,'docs','lcu-api');fs.mkdirSync(out,{recursive:true});
fs.writeFileSync(path.join(out,'LCU接口中文参考.json'),JSON.stringify({meta,endpoints:ops},null,2));
let md=`# LCU 接口中文参考（站点 8.24 快照）\n\n来源：${meta.source} ；抓取日期：${meta.retrievedDate}（用户时区）。\n\n共 **${ops.length}** 个方法＋路径组合，**${meta.uniquePaths}** 个不同路径，按 **${prefixes.length}** 个一级路径前缀分组。原文有说明：${evidenceCount}；路径推断：${ops.length-evidenceCount}。\n\n## 阅读前须知\n\n- 站点明确写的是客户端版本 **8.24**，属于历史快照；不是当前客户端完整清单。\n- “原文有说明”表示站点提供了描述，不表示已在本机或当前游戏版本测试。“路径推断”表示原文缺少说明，中文含义仅依据路径、方法和命名上下文推测。\n- 同一个路径的 GET、POST、PUT、PATCH、DELETE、HEAD 分开列出，旧版、legacy 和不同服务前缀不合并。\n- 本次只读取公开文档，没有调用你的游戏客户端、修改账号、发消息、购买物品或执行游戏操作。\n- GET 通常读取，POST 提交或触发动作，PUT 写入，PATCH 局部修改，DELETE 移除或终止，HEAD 只取响应头。不能只凭方法断定内部副作用或成功条件。\n- 路径中的 {参数} 必须替换为实际值。路径参数类型、查询参数及请求正文应以运行中客户端提供的 schema 为准；页面没有定义的请求体与错误格式不补造。\n- accountId、summonerId 和 PUUID 是不同标识，不应混用。\n- 路径中的 v1/v2/v3 是模块 API 版本，不是游戏补丁版本；高版本不自动意味着可以替换旧接口。\n- 游戏状态、地区、权限与服务开关会限制接口可用性。俱乐部、旧符文册和旧地区限制等历史模块尤其需要重新核实。\n\n## 当前项目优先查阅的接口\n\n|模块|可对照的项目功能|\n|---|---|\n|lol-summoner、lol-ranked|玩家基础资料与段位|\n|lol-match-history、lol-acs|战绩列表、比赛详情与时间线|\n|lol-gameflow|大厅、选人、对局中及赛后阶段检测|\n|lol-champ-select、lol-lobby|自动选禁、召唤师技能、备战席交换与大厅操作|\n|lol-matchmaking|接受对局、匹配状态|\n|lol-perks、lol-item-sets|符文页和装备方案|\n|lol-chat、lol-spectator|好友状态、聊天与观战|\n|lol-replays|回放元数据、下载和观看|\n\n上表是功能映射建议，不表示已核对项目每一处调用。该快照不能完整说明当前海克斯增幅、外部推荐源、本地 OCR 或独立换肤核心；这些也不能等同于标准 LCU 路由。游戏内 Live Client Data API 与本页客户端 API 也应分别核对。\n\n## 模块索引\n\n`;
for(const [i,g] of prefixes.entries()){const arr=grouped[g];md+=`- [${g} · ${arr[0].moduleName}（${arr.length}）](#module-${i+1})\n`;}
for(const [i,g] of prefixes.entries()){const arr=grouped[g];md+=`\n<a id="module-${i+1}"></a>\n\n## ${g} · ${arr[0].moduleName}\n\n${arr[0].moduleAbout}\n\n`;
for(const op of arr){md+=`### ${op.method} ${op.path}\n\n${op.explanation}\n\n- 解释依据：${op.evidence}。\n- 操作性质：${op.effect}。\n- [原文定位](${op.source})\n`;
 if(op.parameters.length){md+='\n|参数|位置|类型|必填|含义|\n|---|---|---|---|---|\n';for(const p of op.parameters)md+=`|${p.name}|${p.location}|${p.type.replace(/\|/g,'\\|')}|${p.required?'是':'未标注'}|${p.note}|\n`;}else md+='\n原页面未列出参数；不等于当前客户端一定不需要请求体或上下文。\n';
 md+=`\n原文列出的响应状态：${op.responses.join('、')||'未列出'}。没有列出的错误码或响应字段不推定。\n`;
}}
fs.writeFileSync(path.join(out,'LCU接口中文参考.md'),md);
fs.writeFileSync(path.join(__dirname,'lcu-reference-data.json'),JSON.stringify({meta,endpoints:ops}));
const template=path.join(__dirname,'lcu-reference-template.html');if(fs.existsSync(template)){const html=fs.readFileSync(template,'utf8').replace('__REFERENCE_DATA__',JSON.stringify({meta,endpoints:ops}).replace(/</g,'\\u003c'));fs.writeFileSync(path.join(out,'index.html'),html);}
console.log(JSON.stringify(meta,null,2));
