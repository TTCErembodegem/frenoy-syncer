using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
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
        // Frenoy GetClubTeams => The Teams within a Club. Each ClubTeam plays in a Division
        // Frenoy Divisions => Prov 3C
        //const string FrenoyVttlWsdlUrl = "http://api.vttl.be/0.7/?wsdl";
        //const string FrenoySportaWsdlUrl = "http://tafeltennis.sporcrea.be/api/?wsdl";
        static void Main(string[] args)
        {
            var options = new FrenoySyncOptions
            {
                FrenoyClub = "OVL134",
                FrenoySeason = "16",
                Jaar = 2015,
                Competitie = "VTTL",
                ReeksType = "Prov"
            };

            using (var vttl = new FrenoySync(options))
            {
                vttl.Sync();
            }

            var sportaOptions = new FrenoySyncOptions
            {
                FrenoyClub = "",
                FrenoySeason = "16",
                Jaar = 2015,
                Competitie = "Sporta",
                ReeksType = "Afd"
            };
        }
    }

    public class FrenoySyncOptions
    {
        public string FrenoyClub { get; set; }
        public string FrenoySeason { get; set; }
        public string Competitie { get; set; }
        public string ReeksType { get; set; }
        public int Jaar { get; set; }
    }

    public class FrenoySync : IDisposable
    {
        private readonly TtcDbContext _db;
        private readonly FrenoySyncOptions _options;
        private readonly TabTAPI_PortTypeClient _frenoy = new FrenoyVttl.TabTAPI_PortTypeClient();
        private readonly int _thuisClubId;

        public FrenoySync(FrenoySyncOptions options)
        {
            _db = new TtcDbContext();
            _options = options;
            _thuisClubId = _db.Clubs.Single(x => x.CodeVTTL == options.FrenoyClub).ID;
        }

        public void Sync()
        {
            var frenoyTeams = _frenoy.GetClubTeams(new GetClubTeamsRequest
            {
                Club = _options.FrenoyClub,
                Season = _options.FrenoySeason
            });

            var reeksRegex = new Regex(@"Afdeling (\d+)(\w+)");
            foreach (var frenoyTeam in frenoyTeams.TeamEntries)
            {
                // Create new division=reeks for each team in the club
                var reeks = new Reeks();
                reeks.Competitie = _options.Competitie;
                reeks.ReeksType = _options.ReeksType;
                reeks.Jaar = _options.Jaar;

                var reeksMatch = reeksRegex.Match(frenoyTeam.DivisionName);
                reeks.ReeksNummer = reeksMatch.Groups[1].Value;
                reeks.ReeksCode = reeksMatch.Groups[2].Value;
                reeks.LinkID = $"{frenoyTeam.DivisionId}_{frenoyTeam.Team}";

                reeks.FrenoyDivisionId = int.Parse(frenoyTeam.DivisionId);
                reeks.FrenoyTeamId = frenoyTeam.TeamId;

                _db.Reeksen.Add(reeks);
                _db.SaveChanges();



                // Create the teams in the new division=reeks
                var frenoyDivision = _frenoy.GetDivisionRanking(new GetDivisionRankingRequest
                {
                    DivisionId = frenoyTeam.DivisionId
                });
                foreach (var frenoyTeamsInDivision in frenoyDivision.RankingEntries)
                {
                    AddClubPloeg(reeks, frenoyTeamsInDivision);
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

                    var kalender = new Kalender
                    {
                        FrenoyMatchId = frenoyMatch.MatchId,
                        Datum = frenoyMatch.Date,
                        Uur = frenoyMatch.Time,
                        ThuisClubID = GetClubId(frenoyMatch.HomeClub),
                        ThuisPloeg = ExtractTeamCodeFromFrenoyName(frenoyMatch.HomeTeam),
                        UitClubID = GetClubId(frenoyMatch.AwayClub),
                        UitPloeg = ExtractTeamCodeFromFrenoyName(frenoyMatch.AwayTeam),
                        UitClubPloegID = 0,
                        Week = int.Parse(frenoyMatch.WeekName)
                    };

                    kalender.ThuisClubPloegID = GetClubPloegId(reeks.ID, kalender.ThuisClubID.Value, kalender.ThuisPloeg);
                    kalender.UitClubPloegID = GetClubPloegId(reeks.ID, kalender.UitClubID.Value, kalender.UitPloeg);

                    kalender.Thuis = kalender.ThuisClubID == _thuisClubId ? 1 : 0;

                    // In the db the ThuisClubId is always Erembodegem
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

                    _db.Kalender.Add(kalender);
                }
                _db.SaveChanges();
            }
        }

        private int GetClubPloegId(int reeksId, int clubId, string ploeg)
        {
            var cb = _db.ClubPloegen.Single(x => x.ReeksId == reeksId && x.ClubId == clubId && x.Code == ploeg);
            return cb.ID;
        }

        private void AddClubPloeg(Reeks reeks, RankingEntryType frenoyTeam)
        {
            var clubPloeg = new ClubPloeg();
            clubPloeg.ReeksId = reeks.ID;
            clubPloeg.ClubId = GetClubId(frenoyTeam.TeamClub);
            clubPloeg.Code = ExtractTeamCodeFromFrenoyName(frenoyTeam.Team);
            _db.ClubPloegen.Add(clubPloeg);
        }

        private static readonly Regex ClubHasTeamCodeRegex = new Regex(@"\w$");
        private static string ExtractTeamCodeFromFrenoyName(string team)
        {
            if (ClubHasTeamCodeRegex.IsMatch(team))
            {
                return team.Substring(team.Length - 1);
            }
            return null;
        }

        private int GetClubId(string frenoyClubCode)
        {
            var club = _db.Clubs.SingleOrDefault(x => x.CodeVTTL == frenoyClubCode);
            if (club == null)
            {
                club = CreateClub(frenoyClubCode);
            }
            return club.ID;
        }

        private Club CreateClub(string frenoyClubCode)
        {
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
