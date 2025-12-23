using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LOL_GameAssistant.Entity
{
    /// <summary>
    /// LOL对局信息主类
    /// </summary>
    public class LobbyGameInfo
    {
        /// <summary>
        /// 是否可以开始游戏活动
        /// </summary>
        [JsonProperty("canStartActivity")]
        public bool CanStartActivity { get; set; }

        /// <summary>
        /// 游戏配置信息
        /// </summary>
        [JsonProperty("gameConfig")]
        public GameConfig GameConfig { get; set; }

        /// <summary>
        /// 邀请信息列表
        /// </summary>
        [JsonProperty("invitations")]
        public List<Invitation> Invitations { get; set; }

        /// <summary>
        /// 本地玩家成员信息
        /// </summary>
        [JsonProperty("localMember")]
        public Member LocalMember { get; set; }

        /// <summary>
        /// 所有成员列表
        /// </summary>
        [JsonProperty("members")]
        public List<Member> Members { get; set; }

        /// <summary>
        /// 多用户聊天JWT数据传输对象
        /// </summary>
        [JsonProperty("mucJwtDto")]
        public MucJwtDto MucJwtDto { get; set; }

        /// <summary>
        /// 多用户聊天室ID
        /// </summary>
        [JsonProperty("multiUserChatId")]
        public string MultiUserChatId { get; set; }

        /// <summary>
        /// 多用户聊天室密码
        /// </summary>
        [JsonProperty("multiUserChatPassword")]
        public string MultiUserChatPassword { get; set; }

        /// <summary>
        /// 派对/房间ID
        /// </summary>
        [JsonProperty("partyId")]
        public string PartyId { get; set; }

        /// <summary>
        /// 派对类型 (open/closed/inviteOnly)
        /// </summary>
        [JsonProperty("partyType")]
        public string PartyType { get; set; }

        /// <summary>
        /// 热门英雄列表
        /// </summary>
        [JsonProperty("popularChampions")]
        public List<object> PopularChampions { get; set; }

        /// <summary>
        /// 限制条件列表
        /// </summary>
        [JsonProperty("restrictions")]
        public List<object> Restrictions { get; set; }

        /// <summary>
        /// 稀缺位置列表
        /// </summary>
        [JsonProperty("scarcePositions")]
        public List<object> ScarcePositions { get; set; }

        /// <summary>
        /// 警告信息列表
        /// </summary>
        [JsonProperty("warnings")]
        public List<object> Warnings { get; set; }
    }

    /// <summary>
    /// 游戏配置信息类
    /// </summary>
    public class GameConfig
    {
        /// <summary>
        /// 允许的预组队规模列表
        /// </summary>
        [JsonProperty("allowablePremadeSizes")]
        public List<int> AllowablePremadeSizes { get; set; }

        /// <summary>
        /// 自定义大厅名称
        /// </summary>
        [JsonProperty("customLobbyName")]
        public string CustomLobbyName { get; set; }

        /// <summary>
        /// 自定义游戏变异器名称
        /// </summary>
        [JsonProperty("customMutatorName")]
        public string CustomMutatorName { get; set; }

        /// <summary>
        /// 自定义奖励禁用原因列表
        /// </summary>
        [JsonProperty("customRewardsDisabledReasons")]
        public List<object> CustomRewardsDisabledReasons { get; set; }

        /// <summary>
        /// 自定义观战策略
        /// </summary>
        [JsonProperty("customSpectatorPolicy")]
        public string CustomSpectatorPolicy { get; set; }

        /// <summary>
        /// 自定义观战者列表
        /// </summary>
        [JsonProperty("customSpectators")]
        public List<object> CustomSpectators { get; set; }

        /// <summary>
        /// 自定义队伍100（蓝色方）成员列表
        /// </summary>
        [JsonProperty("customTeam100")]
        public List<Member> CustomTeam100 { get; set; }

        /// <summary>
        /// 自定义队伍200（红色方）成员列表
        /// </summary>
        [JsonProperty("customTeam200")]
        public List<Member> CustomTeam200 { get; set; }

        /// <summary>
        /// 游戏模式
        /// </summary>
        [JsonProperty("gameMode")]
        public string GameMode { get; set; }

        /// <summary>
        /// 是否为自定义游戏
        /// </summary>
        [JsonProperty("isCustom")]
        public bool IsCustom { get; set; }

        /// <summary>
        /// 大厅是否已满
        /// </summary>
        [JsonProperty("isLobbyFull")]
        public bool IsLobbyFull { get; set; }

        /// <summary>
        /// 是否由团队构建器管理
        /// </summary>
        [JsonProperty("isTeamBuilderManaged")]
        public bool IsTeamBuilderManaged { get; set; }

        /// <summary>
        /// 地图ID
        /// </summary>
        [JsonProperty("mapId")]
        public int MapId { get; set; }

        /// <summary>
        /// 最大人类玩家数量
        /// </summary>
        [JsonProperty("maxHumanPlayers")]
        public int MaxHumanPlayers { get; set; }

        /// <summary>
        /// 最大大厅规模
        /// </summary>
        [JsonProperty("maxLobbySize")]
        public int MaxLobbySize { get; set; }

        /// <summary>
        /// 最大观战者数量
        /// </summary>
        [JsonProperty("maxLobbySpectatorCount")]
        public int MaxLobbySpectatorCount { get; set; }

        /// <summary>
        /// 最大队伍规模
        /// </summary>
        [JsonProperty("maxTeamSize")]
        public int MaxTeamSize { get; set; }

        /// <summary>
        /// 每队玩家数量
        /// </summary>
        [JsonProperty("numPlayersPerTeam")]
        public int NumPlayersPerTeam { get; set; }

        /// <summary>
        /// 大厅中的队伍数量
        /// </summary>
        [JsonProperty("numberOfTeamsInLobby")]
        public int NumberOfTeamsInLobby { get; set; }

        /// <summary>
        /// 选择类型
        /// </summary>
        [JsonProperty("pickType")]
        public string PickType { get; set; }

        /// <summary>
        /// 是否允许预组队规模
        /// </summary>
        [JsonProperty("premadeSizeAllowed")]
        public bool PremadeSizeAllowed { get; set; }

        /// <summary>
        /// 队列ID
        /// </summary>
        [JsonProperty("queueId")]
        public int QueueId { get; set; }

        /// <summary>
        /// 是否强制选择稀缺位置
        /// </summary>
        [JsonProperty("shouldForceScarcePositionSelection")]
        public bool ShouldForceScarcePositionSelection { get; set; }

        /// <summary>
        /// 是否显示位置选择器
        /// </summary>
        [JsonProperty("showPositionSelector")]
        public bool ShowPositionSelector { get; set; }

        /// <summary>
        /// 是否显示快速游戏槽位选择
        /// </summary>
        [JsonProperty("showQuickPlaySlotSelection")]
        public bool ShowQuickPlaySlotSelection { get; set; }
    }

    /// <summary>
    /// 成员/玩家信息类（可用于人类玩家和机器人）
    /// </summary>
    public class Member
    {
        /// <summary>
        /// 是否允许更改活动
        /// </summary>
        [JsonProperty("allowedChangeActivity")]
        public bool AllowedChangeActivity { get; set; }

        /// <summary>
        /// 是否允许邀请他人
        /// </summary>
        [JsonProperty("allowedInviteOthers")]
        public bool AllowedInviteOthers { get; set; }

        /// <summary>
        /// 是否允许踢出他人
        /// </summary>
        [JsonProperty("allowedKickOthers")]
        public bool AllowedKickOthers { get; set; }

        /// <summary>
        /// 是否允许开始活动
        /// </summary>
        [JsonProperty("allowedStartActivity")]
        public bool AllowedStartActivity { get; set; }

        /// <summary>
        /// 是否允许切换邀请权限
        /// </summary>
        [JsonProperty("allowedToggleInvite")]
        public bool AllowedToggleInvite { get; set; }

        /// <summary>
        /// 是否有自动补位资格
        /// </summary>
        [JsonProperty("autoFillEligible")]
        public bool AutoFillEligible { get; set; }

        /// <summary>
        /// 是否为晋级赛自动补位保护
        /// </summary>
        [JsonProperty("autoFillProtectedForPromos")]
        public bool AutoFillProtectedForPromos { get; set; }

        /// <summary>
        /// 是否为补救自动补位保护
        /// </summary>
        [JsonProperty("autoFillProtectedForRemedy")]
        public bool AutoFillProtectedForRemedy { get; set; }

        /// <summary>
        /// 是否为单排自动补位保护
        /// </summary>
        [JsonProperty("autoFillProtectedForSoloing")]
        public bool AutoFillProtectedForSoloing { get; set; }

        /// <summary>
        /// 是否为连胜自动补位保护
        /// </summary>
        [JsonProperty("autoFillProtectedForStreaking")]
        public bool AutoFillProtectedForStreaking { get; set; }

        /// <summary>
        /// 机器人英雄ID（0表示非机器人或未选择英雄）
        /// </summary>
        [JsonProperty("botChampionId")]
        public int BotChampionId { get; set; }

        /// <summary>
        /// 机器人难度等级
        /// </summary>
        [JsonProperty("botDifficulty")]
        public string BotDifficulty { get; set; }

        /// <summary>
        /// 机器人ID
        /// </summary>
        [JsonProperty("botId")]
        public string BotId { get; set; }

        /// <summary>
        /// 机器人位置
        /// </summary>
        [JsonProperty("botPosition")]
        public string BotPosition { get; set; }

        /// <summary>
        /// 机器人UUID
        /// </summary>
        [JsonProperty("botUuid")]
        public string BotUuid { get; set; }

        /// <summary>
        /// 首选位置
        /// </summary>
        [JsonProperty("firstPositionPreference")]
        public string FirstPositionPreference { get; set; }

        /// <summary>
        /// 子队内位置（通常为null）
        /// </summary>
        [JsonProperty("intraSubteamPosition")]
        public object IntraSubteamPosition { get; set; }

        /// <summary>
        /// 是否为机器人
        /// </summary>
        [JsonProperty("isBot")]
        public bool IsBot { get; set; }

        /// <summary>
        /// 是否为队长
        /// </summary>
        [JsonProperty("isLeader")]
        public bool IsLeader { get; set; }

        /// <summary>
        /// 是否为观战者
        /// </summary>
        [JsonProperty("isSpectator")]
        public bool IsSpectator { get; set; }

        /// <summary>
        /// 成员数据（通常为null）
        /// </summary>
        [JsonProperty("memberData")]
        public object MemberData { get; set; }

        /// <summary>
        /// 玩家槽位列表
        /// </summary>
        [JsonProperty("playerSlots")]
        public List<object> PlayerSlots { get; set; }

        /// <summary>
        /// 玩家唯一标识符
        /// </summary>
        [JsonProperty("puuid")]
        public string Puuid { get; set; }

        /// <summary>
        /// 是否准备就绪
        /// </summary>
        [JsonProperty("ready")]
        public bool Ready { get; set; }

        /// <summary>
        /// 次选位置
        /// </summary>
        [JsonProperty("secondPositionPreference")]
        public string SecondPositionPreference { get; set; }

        /// <summary>
        /// 是否显示被踢出横幅
        /// </summary>
        [JsonProperty("showGhostedBanner")]
        public bool ShowGhostedBanner { get; set; }

        /// <summary>
        /// 草莓地图ID（通常为null）
        /// </summary>
        [JsonProperty("strawberryMapId")]
        public object StrawberryMapId { get; set; }

        /// <summary>
        /// 子队索引（通常为null）
        /// </summary>
        [JsonProperty("subteamIndex")]
        public object SubteamIndex { get; set; }

        /// <summary>
        /// 召唤师头像ID
        /// </summary>
        [JsonProperty("summonerIconId")]
        public int SummonerIconId { get; set; }

        /// <summary>
        /// 召唤师ID
        /// </summary>
        [JsonProperty("summonerId")]
        public long SummonerId { get; set; }

        /// <summary>
        /// 召唤师内部名称
        /// </summary>
        [JsonProperty("summonerInternalName")]
        public string SummonerInternalName { get; set; }

        /// <summary>
        /// 召唤师等级
        /// </summary>
        [JsonProperty("summonerLevel")]
        public int SummonerLevel { get; set; }

        /// <summary>
        /// 召唤师名称
        /// </summary>
        [JsonProperty("summonerName")]
        public string SummonerName { get; set; }

        /// <summary>
        /// 队伍ID
        /// </summary>
        [JsonProperty("teamId")]
        public int TeamId { get; set; }
    }

    /// <summary>
    /// 邀请信息类
    /// </summary>
    public class Invitation
    {
        /// <summary>
        /// 邀请ID
        /// </summary>
        [JsonProperty("invitationId")]
        public string InvitationId { get; set; }

        /// <summary>
        /// 邀请类型
        /// </summary>
        [JsonProperty("invitationType")]
        public string InvitationType { get; set; }

        /// <summary>
        /// 邀请状态 (Accepted/Declined/Pending)
        /// </summary>
        [JsonProperty("state")]
        public string State { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        [JsonProperty("timestamp")]
        public string Timestamp { get; set; }

        /// <summary>
        /// 目标召唤师ID
        /// </summary>
        [JsonProperty("toSummonerId")]
        public long ToSummonerId { get; set; }

        /// <summary>
        /// 目标召唤师名称
        /// </summary>
        [JsonProperty("toSummonerName")]
        public string ToSummonerName { get; set; }
    }

    /// <summary>
    /// 多用户聊天JWT数据传输对象
    /// </summary>
    public class MucJwtDto
    {
        /// <summary>
        /// 频道声明/ID
        /// </summary>
        [JsonProperty("channelClaim")]
        public string ChannelClaim { get; set; }

        /// <summary>
        /// 域名
        /// </summary>
        [JsonProperty("domain")]
        public string Domain { get; set; }

        /// <summary>
        /// JSON Web Token
        /// </summary>
        [JsonProperty("jwt")]
        public string Jwt { get; set; }

        /// <summary>
        /// 目标地区/服务器
        /// </summary>
        [JsonProperty("targetRegion")]
        public string TargetRegion { get; set; }
    }

    /// <summary>
    /// 游戏位置枚举（根据JSON数据推断）
    /// </summary>
    public enum GamePosition
    {
        NONE,
        TOP,
        JUNGLE,
        MIDDLE,
        BOTTOM,
        UTILITY,
        FILL
    }

    /// <summary>
    /// 机器人难度枚举（根据JSON数据推断）
    /// </summary>
    public enum BotDifficulty
    {
        NONE,
        RSEASY,
        RSINTERMEDIATE,
        RSHARD,
        RSUBERHARD
    }

    /// <summary>
    /// 游戏模式枚举（根据JSON数据推断）
    /// </summary>
    public enum GameMode
    {
        PRACTICETOOL,
        CLASSIC,
        ARAM,
        URF,
        ONEFORALL,
        NEXUSBLITZ
    }
}