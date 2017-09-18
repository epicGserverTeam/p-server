using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Netsphere.Network.Message.GameRule;
using Netsphere.Network.Data.GameRule;

namespace Netsphere.Game.GameRules
{
    internal class CaptainGameRule : GameRuleBase
    {
        public override GameRule GameRule => GameRule.Captain;
        public override Briefing Briefing { get; }

        private TimeSpan _roundTime = TimeSpan.Zero;
        private bool IsNewRound = true;
        private int Rounds { get; set; }
        private int RoundsPlayed { get; set; }

        public CaptainGameRule(Room room)
            : base(room)
        {
            Briefing = new Briefing(this);
            
            Rounds = (int)room.Options.ScoreLimit == 3 ? 3 : 5;

            StateMachine.Configure(GameRuleState.Waiting)
                .PermitIf(GameRuleStateTrigger.StartGame, GameRuleState.FirstHalf, CanStartGame);

            StateMachine.Configure(GameRuleState.FirstHalf)
                .SubstateOf(GameRuleState.Playing)
                .Permit(GameRuleStateTrigger.StartResult, GameRuleState.EnteringResult);

            StateMachine.Configure(GameRuleState.EnteringResult)
                .SubstateOf(GameRuleState.Playing)
                .Permit(GameRuleStateTrigger.StartResult, GameRuleState.Result);

            StateMachine.Configure(GameRuleState.Result)
                .SubstateOf(GameRuleState.Playing)
                .Permit(GameRuleStateTrigger.EndGame, GameRuleState.Waiting);

        }

        public override void Initialize()
        {
            var teamMgr = Room.TeamManager;
            teamMgr.Add(Team.Alpha, (uint)(Room.Options.MatchKey.PlayerLimit / 2), (uint)(Room.Options.MatchKey.SpectatorLimit / 2));
            teamMgr.Add(Team.Beta, (uint)(Room.Options.MatchKey.PlayerLimit / 2), (uint)(Room.Options.MatchKey.SpectatorLimit / 2));

            base.Initialize();
        }

        public override void Cleanup()
        {
            var teamMgr = Room.TeamManager;
            teamMgr.Remove(Team.Alpha);
            teamMgr.Remove(Team.Beta);

            base.Cleanup();
        }

        /**
         * 3 minutes time per round
         * 2 minutes 30 seconds is the actual time per round
         */
        public override void Update(TimeSpan delta)
        {
            base.Update(delta);
            _roundTime += delta;

            var teamMgr = Room.TeamManager;

            if (StateMachine.IsInState(GameRuleState.Playing) &&
                !StateMachine.IsInState(GameRuleState.EnteringResult) &&
                !StateMachine.IsInState(GameRuleState.Result))
            {

                // TODO: Add dispersion 
                if(IsNewRound)
                {
                    int playingPlayers = Room.TeamManager.PlayersPlaying.Count();
                    CaptainLifeDto[] captains = new CaptainLifeDto[playingPlayers];

                    int count = 0;
                    foreach (Player plr in Room.TeamManager.PlayersPlaying)
                    {
                        CaptainLifeDto captain = new CaptainLifeDto();

                        captain.AccountId = plr.Account.Id;
                        captain.HP = 1000;
                        captains[count] = captain;

                        count++;
                    }
                    Room.Broadcast(new SCaptainLifeRoundSetUpAckMessage(captains));
                    IsNewRound = false;
                }

                if (StateMachine.IsInState(GameRuleState.FirstHalf))
                {
                    if (!IsNewRound)
                    {
                        // Still have enough players?
                        var min = teamMgr.Values.Min(team =>
                        team.Values.Count(plr =>
                            plr.RoomInfo.State != PlayerState.Lobby &&
                            plr.RoomInfo.State != PlayerState.Spectating));
                        if (min == 0 && !IsThereAnDeveloper())
                            StateMachine.Fire(GameRuleStateTrigger.StartResult);

                        // Did we reach round limit?
                        if (RoundsPlayed == Rounds)
                            StateMachine.Fire(GameRuleStateTrigger.StartResult);

                        if (_roundTime >= RoundTime)
                        { 
                            _roundTime = TimeSpan.Zero;
                            Room.Broadcast(new SCaptainSubRoundEndReasonAckMessage());
                        }
                    }
                }
            }
        }

        private bool CanStartGame()
        {
            if (!StateMachine.IsInState(GameRuleState.Waiting))
                return false;

            var teams = Room.TeamManager.Values.ToArray();
            if (teams.Any(team => team.Count == 0) && !IsThereAnDeveloper()) // Do we have enough players?
                return false;

            // Is atleast one player per team ready?
            return IsThereAnDeveloper() || teams.All(team => team.Players.Any(plr => plr.RoomInfo.IsReady || Room.Master == plr));
        }

        public override PlayerRecord GetPlayerRecord(Player plr)
        {
            return new CaptainPlayerRecord(plr);
        }


    }

    internal class CaptainPlayerRecord : PlayerRecord
    {
        public override uint TotalScore => GetTotalScore();

        public int KillPoints { get; set; }
        public int KillPointsAssists { get; set; }
        public int HealPointsAssists { get; set; }
        public int Unk { get; set; }
        public int Unk2 { get; set; }
        public int Unk3 { get; set; }
        public int Unk4 { get; set; }
        public int CaptainKills { get; set; }
        public int Dominaion { get; set; }
        public int Death { get; set; }

        public CaptainPlayerRecord(Player plr)
            : base(plr)
        { }

        public override void Serialize(BinaryWriter w, bool isResult)
        {
            base.Serialize(w, isResult);

            w.Write(KillPoints);
            w.Write(KillPointsAssists);
            w.Write(HealPointsAssists);
            w.Write(Unk);
            w.Write(Unk2);
            w.Write(Unk3);
            w.Write(Unk4);
            w.Write(CaptainKills);
            w.Write(Dominaion);
            w.Write(Death);

        }

        private uint GetTotalScore()
        {
            return (uint)(KillPoints * 2 + KillPointsAssists
                + CaptainKills * 5 + HealPointsAssists);
        }

        public override void Reset()
        {
            base.Reset();

            KillPoints = 10;
            KillPointsAssists = 5;
            HealPointsAssists = 1;
            Unk = 0;
            Unk2 = 0;
            Unk3 = 0;
            Unk4 = 0;
            CaptainKills = 2;
            Dominaion = 0;
            Death = 0;
        }

    }
}
