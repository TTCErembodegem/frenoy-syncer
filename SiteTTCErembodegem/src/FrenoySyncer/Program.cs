using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.ServiceModel;
using System.ServiceModel.Description;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Ttc.DataAccess;
using FrenoySyncer.FrenoyVttl;
using Ttc.Model;

namespace FrenoySyncer
{
    class Program
    {
        static void Main(string[] args)
        {
            var options = new FrenoySyncOptions
            {
                FrenoyClub = "OVL134",
                FrenoySeason = "16",
                Jaar = 2015,
                Competitie = "VTTL",
                ReeksType = "Prov",
                Players = new Dictionary<string, string[]>
                {
                    ["A"] = new[] { "Dirk DS.", "Kharmis", "Jorn", "Sami", "Jurgen E.", "Wouter" },
                    ["B"] = new[] { "Bart", "Gerdo", "Jens", "Dimitri", "Patrick", "Geert" },
                    ["C"] = new[] { "Thomas", "Dirk B", "Jelle", "Arne", "Laurens", "Hugo" },
                    ["D"] = new[] { "Jan", "Marc", "Luc", "Maarten", "Veerle", "Patrick DS" },
                    ["E"] = new[] { "Dirk K.", "Leo", "Dries", "Guy", "Peter N", "Tuur", "Peter V" },
                    ["F"] = new[] { "Tim", "Etienne", "Thierry", "Rudi", "Marnix", "Daniel", "Wim" }
                }
            };

            using (var vttl = new FrenoySync(options))
            {
                vttl.Sync();
            }

            var sportaOptions = new FrenoySyncOptions
            {
                FrenoyClub = "4055",
                FrenoySeason = "16",
                Jaar = 2015,
                Competitie = "Sporta",
                ReeksType = "Afd",
                Players = new Dictionary<string, string[]>
                {
                    ["A"] = new[] { "Dirk DS.", "Kharmis", "Jorn", "Sami", "Wouter" },
                    ["B"] = new[] { "Bart", "Patrick", "Geert", "Dirk B", "Jelle" },
                    ["C"] = new[] { "Dries", "Maarten", "Luc", "Jan", "Veerle" },
                    ["D"] = new[] { "Leo", "Guy", "Patrick", "Tuur", "Peter V" },
                    ["E"] = new[] { "Dirk K.", "Etienne", "Peter N", "Marnix", "Thierry", "Martin" },
                    ["F"] = new[] { "Tim", "Rudi", "Daniel", "Tim", "Etienne C.", "Myriam", "Wim", "Martin" }
                }
            };

            using (var sporta = new FrenoySync(sportaOptions, false))
            {
                sporta.Sync();
            }
        }
    }

    /// <summary>
    /// Frenoy GetClubTeams => The Teams within a Club. Each ClubTeam plays in a Division
    /// Frenoy Divisions => Prov 3C
    /// </summary>
    public class FrenoySyncOptions
    {
        public string FrenoyClub { get; set; }
        public string FrenoySeason { get; set; }
        public string Competitie { get; set; }
        public string ReeksType { get; set; }
        public int Jaar { get; set; }

        /// <summary>
        /// Keys = TeamCode
        /// Values = Players with first player = Captain
        /// </summary>
        public Dictionary<string, string[]> Players { get; set; }
    }

    public class FrenoySync : IDisposable
    {
        const string FrenoyVttlWsdlUrl = "http://api.vttl.be/0.7/?wsdl";
        const string FrenoySportaWsdlUrl = "http://tafeltennis.sporcrea.be/api/?wsdl";

        private readonly TtcDbContext _db;
        private readonly FrenoySyncOptions _options;
        private readonly TabTAPI_PortTypeClient _frenoy;
        private readonly int _thuisClubId;
        private readonly bool _isVttl;

        public FrenoySync(FrenoySyncOptions options, bool isVttl = true)
        {
            _db = new TtcDbContext();
            _db.Database.Log = Console.Write;
            CheckPlayers();

            _options = options;
            _isVttl = isVttl;

            string wsdl;
            if (isVttl)
            {
                _thuisClubId = _db.Clubs.Single(x => x.CodeVTTL == options.FrenoyClub).ID;
                wsdl = FrenoyVttlWsdlUrl;
            }
            else
            {
                // Sporta
                _thuisClubId = _db.Clubs.Single(x => x.CodeSporta == options.FrenoyClub).ID;
                wsdl = FrenoySportaWsdlUrl;
            }

            //var sportaBinding = new BasicHttpBinding();
            //sportaBinding.Security.Mode = BasicHttpSecurityMode.None;
            //var sportaEndpoint = new EndpointAddress(wsdl);
            //_frenoy = new TabTAPI_PortTypeClient(sportaBinding, sportaEndpoint);
            _frenoy = new FrenoyVttl.TabTAPI_PortTypeClient();
        }

        [Conditional("DEBUG")]
        private void CheckPlayers()
        {
            foreach (string player in _options.Players.Values.SelectMany(x => x))
            {
                try
                {
                    GetSpelerId(player);
                }
                catch (Exception)
                {
                    throw new Exception("No player with NaamKort " + player);
                }
            }
        }

        public void Sync()
        {
            var frenoyTeams = _frenoy.GetClubTeams(new GetClubTeamsRequest
            {
                Club = _options.FrenoyClub,
                Season = _options.FrenoySeason
            });

            return;

            foreach (var frenoyTeam in frenoyTeams.TeamEntries.Take(1))
            {
                // Create new division=reeks for each team in the club
                Reeks reeks = CreateReeks(frenoyTeam);
                _db.Reeksen.Add(reeks);
                _db.SaveChanges();



                // Create the teams in the new division=reeks
                var frenoyDivision = _frenoy.GetDivisionRanking(new GetDivisionRankingRequest
                {
                    DivisionId = frenoyTeam.DivisionId
                });
                foreach (var frenoyTeamsInDivision in frenoyDivision.RankingEntries)
                {
                    var clubPloeg = CreateClubPloeg(reeks, frenoyTeamsInDivision);
                    _db.ClubPloegen.Add(clubPloeg);
                }
                _db.SaveChanges();



                // Add players to the home team
                var ploeg = _db.ClubPloegen.Single(x => x.ClubId == _thuisClubId && x.ReeksId == reeks.ID && x.Code == frenoyTeam.Team);
                var players = _options.Players[ploeg.Code];
                foreach (var playerName in players)
                {
                    var clubPloegSpeler = new ClubPloegSpeler
                    {
                        Kapitein = playerName == players.First() ? 1 : 0,
                        SpelerID = GetSpelerId(playerName),
                        ClubPloegID = ploeg.ID
                    };
                    _db.ClubPloegSpelers.Add(clubPloegSpeler);
                }
                _db.SaveChanges();



                // Create the matches=kalender table in the new  division=reeks
                var matches = _frenoy.GetMatches(new GetMatchesRequest
                {
                    Club = _options.FrenoyClub,
                    Season = _options.FrenoySeason,
                    DivisionId = reeks.FrenoyDivisionId.ToString()
                });
                foreach (var frenoyMatch in matches.TeamMatchesEntries.Where(x => x.HomeTeam.Trim() != "Vrij" && x.AwayTeam.Trim() != "Vrij"))
                {
                    Debug.Assert(frenoyMatch.DateSpecified);
                    Debug.Assert(frenoyMatch.TimeSpecified);

                    Kalender kalender = CreateKalenderMatch(reeks, frenoyMatch);
                    _db.Kalender.Add(kalender);
                }
                _db.SaveChanges();
            }
        }

        private int GetSpelerId(string playerName)
        {
            var speler = _db.Spelers.Single(x => x.NaamKort == playerName);
            return speler.ID;
        }

        private readonly static Regex VttlReeksRegex = new Regex(@"Afdeling (\d+)(\w+)");
        private readonly static Regex SportaReeksRegex = new Regex(@"^(\d)(\w?)");
        private Reeks CreateReeks(TeamEntryType frenoyTeam)
        {
            var reeks = new Reeks();
            reeks.Competitie = _options.Competitie;
            reeks.ReeksType = _options.ReeksType;
            reeks.Jaar = _options.Jaar;
            reeks.LinkID = $"{frenoyTeam.DivisionId}_{frenoyTeam.Team}";

            if (_isVttl)
            {
                var reeksMatch = VttlReeksRegex.Match(frenoyTeam.DivisionName);
                reeks.ReeksNummer = reeksMatch.Groups[1].Value;
                reeks.ReeksCode = reeksMatch.Groups[2].Value;
            }
            else
            {
                var reeksMatch = SportaReeksRegex.Match(frenoyTeam.DivisionName.Trim());
                reeks.ReeksNummer = reeksMatch.Groups[1].Value;
                reeks.ReeksCode = reeksMatch.Groups[2].Value;
            }

            reeks.FrenoyDivisionId = int.Parse(frenoyTeam.DivisionId);
            reeks.FrenoyTeamId = frenoyTeam.TeamId;
            return reeks;
        }

        private Kalender CreateKalenderMatch(Reeks reeks, TeamMatchEntryType frenoyMatch)
        {
            var kalender = new Kalender
            {
                FrenoyMatchId = frenoyMatch.MatchId,
                Datum = frenoyMatch.Date,
                Uur = frenoyMatch.Time,
                ThuisClubID = GetClubId(frenoyMatch.HomeClub),
                ThuisPloeg = ExtractTeamCodeFromFrenoyName(frenoyMatch.HomeTeam),
                UitClubID = GetClubId(frenoyMatch.AwayClub),
                UitPloeg = ExtractTeamCodeFromFrenoyName(frenoyMatch.AwayTeam),
                Week = int.Parse(frenoyMatch.WeekName)
            };

            kalender.ThuisClubPloegID = GetClubPloegId(reeks.ID, kalender.ThuisClubID.Value, kalender.ThuisPloeg);
            kalender.UitClubPloegID = GetClubPloegId(reeks.ID, kalender.UitClubID.Value, kalender.UitPloeg);

            // In the db the ThuisClubId is always Erembodegem
            kalender.Thuis = kalender.ThuisClubID == _thuisClubId ? 1 : 0;
            if (kalender.Thuis == 1)
            {
                var thuisClubId = kalender.ThuisClubID;
                var thuisPloeg = kalender.ThuisPloeg;
                var thuisClubPloegId = kalender.ThuisClubPloegID;

                kalender.ThuisClubID = kalender.UitClubID;
                kalender.ThuisPloeg = kalender.UitPloeg;
                kalender.ThuisClubPloegID = kalender.UitClubPloegID;

                kalender.UitClubID = thuisClubId;
                kalender.UitPloeg = thuisPloeg;
                kalender.UitClubPloegID = thuisClubPloegId;
            }

            return kalender;
        }

        private int GetClubPloegId(int reeksId, int clubId, string ploeg)
        {
            var cb = _db.ClubPloegen.Single(x => x.ReeksId == reeksId && x.ClubId == clubId && x.Code == ploeg);
            return cb.ID;
        }

        private ClubPloeg CreateClubPloeg(Reeks reeks, RankingEntryType frenoyTeam)
        {
            var clubPloeg = new ClubPloeg();
            clubPloeg.ReeksId = reeks.ID;
            clubPloeg.ClubId = GetClubId(frenoyTeam.TeamClub);
            clubPloeg.Code = ExtractTeamCodeFromFrenoyName(frenoyTeam.Team);
            return clubPloeg;
        }

        private static readonly Regex ClubHasTeamCodeRegex = new Regex(@"\w$");
        private static string ExtractTeamCodeFromFrenoyName(string team)
        {
            if (ClubHasTeamCodeRegex.IsMatch(team))
            {
                return team.Substring(team.Length - 1);
            }
            Debug.Assert(false, "This code path is never been tested");
            return null;
        }

        private int GetClubId(string frenoyClubCode)
        {
            Club club;
            if (_isVttl)
            {
                club = _db.Clubs.SingleOrDefault(x => x.CodeVTTL == frenoyClubCode);
            }
            else
            {
                club = _db.Clubs.SingleOrDefault(x => x.CodeSporta == frenoyClubCode);
            }
            if (club == null)
            {
                club = CreateClub(frenoyClubCode);
            }
            return club.ID;
        }

        private Club CreateClub(string frenoyClubCode)
        {
            Debug.Assert(_isVttl, "or need to write an if");
            var frenoyClub = _frenoy.GetClubs(new GetClubs
            {
                Club = frenoyClubCode,
                Season = _options.FrenoySeason
            });
            Debug.Assert(frenoyClub.ClubEntries.Count() == 1);

            var club = new Club
            {
                CodeVTTL = frenoyClubCode,
                Actief = 1,
                Naam = frenoyClub.ClubEntries.First().LongName,
                Douche = 0
            };
            _db.Clubs.Add(club);
            _db.SaveChanges();

            foreach (var frenoyLokaal in frenoyClub.ClubEntries.First().VenueEntries)
            {
                var lokaal = new ClubLokaal
                {
                    ClubId = club.ID,
                    Telefoon = frenoyLokaal.Phone,
                    Lokaal = frenoyLokaal.Name,
                    Adres = frenoyLokaal.Street,
                    Postcode = int.Parse(frenoyLokaal.Town.Substring(0, frenoyLokaal.Town.IndexOf(" "))),
                    Gemeente = frenoyLokaal.Town.Substring(frenoyLokaal.Town.IndexOf(" ") + 1),
                    Hoofd = 1
                };
                _db.ClubLokalen.Add(lokaal);
            }

            return club;
        }

        public void Dispose()
        {
            _db.Dispose();
        }
    }
}