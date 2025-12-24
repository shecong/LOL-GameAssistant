using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace LOL_GameAssistant.Entity
{
    public class GameSessionResponse
    {
        [JsonPropertyName("phase")]
        public string Phase { get; set; }

        [JsonPropertyName("gameData")]
        public GameData GameData { get; set; }
    }

    public class GameData
    {
        [JsonPropertyName("teamOne")]
        public List<TeamMember> TeamOne { get; set; }

        [JsonPropertyName("teamTwo")]
        public List<TeamMember> TeamTwo { get; set; }
    }

    public class TeamMember
    {
        [JsonPropertyName("championId")]
        public int ChampionId { get; set; }

        [JsonPropertyName("lastSelectedSkinIndex")]
        public int LastSelectedSkinIndex { get; set; }

        [JsonPropertyName("profileIconId")]
        public int ProfileIconId { get; set; }

        [JsonPropertyName("puuid")]
        public string Puuid { get; set; }

        [JsonPropertyName("selectedPosition")]
        public string SelectedPosition { get; set; }

        [JsonPropertyName("selectedRole")]
        public string SelectedRole { get; set; }

        [JsonPropertyName("summonerId")]
        public long SummonerId { get; set; }

        [JsonPropertyName("summonerInternalName")]
        public string SummonerInternalName { get; set; }

        [JsonPropertyName("summonerName")]
        public string SummonerName { get; set; }

        [JsonPropertyName("teamOwner")]
        public bool TeamOwner { get; set; }

        [JsonPropertyName("teamParticipantId")]
        public int TeamParticipantId { get; set; }
    }

}
