using System.Threading.Tasks;
using Assets.__Scripts.Loading.Replays.PP;
using Assets.__Scripts.Loading.Replays.ScoreSaber.Utils;
using UnityEngine;

public static class ScoreSaberSource
{
    public static ScoreSaberPPHandler PPHandler { get; private set;}

    public static ReplaySourceInfo Create(ScoreSaberScoreResponse response = null)
    {
        ScoreSaberPlayerInfo player = response?.GetPlayer();
        ScoreSaberLeaderboardInfo leaderboard = response?.leaderboard;
        ReplaySourceInfo info = new ReplaySourceInfo
        {
            SourceType = ReplaySourceType.ScoreSaber,
            MapHash = leaderboard?.map?.hash,
            DifficultyRaw = leaderboard?.difficulty?.difficulty ?? 0,
            Characteristic = CharacteristicFromGameMode(leaderboard?.difficulty?.gameMode),
            MapID = leaderboard?.map?.bsid,
            PlayerName = player?.name,
            PlayerID = player?.id,
            AvatarURL = player?.avatar
        };

        if(!string.IsNullOrEmpty(info.PlayerID))
        {
            info.PlayerProfileURL = $"{ApiConfig.ScoreSaberBaseURL}u/{info.PlayerID}";
        }

        if(leaderboard?.map?.id > 0 && leaderboard.id > 0)
        {
            info.LeaderboardURL = $"{ApiConfig.ScoreSaberBaseURL}map/{leaderboard.map.id}/difficulty/{leaderboard.id}";
        }

        info.LoadSourceData = replay => LoadSourceDataAsync(info, replay);

        if(response?.leaderboard?.realm != null)
        {
            if(PPHandler == null)
            {
                PPHandler = new ScoreSaberPPHandler(response.leaderboard.realm.stars);
                PPManager.RegisterProvider(PPHandler);
            }
            else
            {
                PPHandler.SetScoreSaberStars(response.leaderboard.realm.stars);
            }
        }

        return info;
    }


    public static async Task<ResolvedScore> ResolveScoreAsync(string scoreID, string mapURL, string mapID, bool showErrors = true)
    {
        ScoreSaberScoreResponse apiResponse = await ScoreSaberApi.ScoreFromID(scoreID, showErrors);
        if(apiResponse == null || apiResponse.score == null || !apiResponse.score.hasReplay)
        {
            return null;
        }

        ReplaySourceInfo sourceInfo = Create(apiResponse);

        if(string.IsNullOrEmpty(mapID))
        {
            mapID = apiResponse.leaderboard?.map?.bsid;
        }

        return new ResolvedScore
        {
            ReplayURL = ScoreSaberApi.ReplayURLFromID(apiResponse.score?.id > 0 ? apiResponse.score.id.ToString() : scoreID),
            MapURL = mapURL,
            MapID = mapID,
            SourceInfo = sourceInfo
        };
    }


    public static async Task LoadSourceDataAsync(ReplaySourceInfo source, Replay replay)
    {
        if(source == null || replay == null)
        {
            return;
        }

        if(!string.IsNullOrEmpty(replay.info?.playerID))
        {
            source.PlayerID = replay.info.playerID;
            source.PlayerProfileURL = $"{ApiConfig.ScoreSaberBaseURL}u/{source.PlayerID}";
        }

        if(string.IsNullOrEmpty(source.AvatarURL)) return;

        string avatarUrl = source.AvatarURL;
#if UNITY_WEBGL && !UNITY_EDITOR
        avatarUrl = WebLoader.GetCorsURL(avatarUrl);
#endif
        byte[] avatarData = await ReplayLoader.DownloadAvatarData(avatarUrl);
        if(avatarData != null && avatarData.Length > 0)
        {
            ReplayManager.SetAvatarImageData(avatarData);
        }
    }


    private static string CharacteristicFromGameMode(string gameMode)
    {
        if(string.IsNullOrEmpty(gameMode)) return null;
        if(!gameMode.StartsWith("Solo")) return gameMode;

        string characteristic = gameMode.Substring(4);
        return string.IsNullOrEmpty(characteristic) ? "Standard" : characteristic;
    }
}
