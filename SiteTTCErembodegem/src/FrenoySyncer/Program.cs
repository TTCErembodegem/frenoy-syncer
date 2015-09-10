using System;
using System.Collections.Generic;
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
            using (var db = new TtcDbContext())
            {
                //var spelers = db.Spelers.ToArray();

                var frenoy = new FrenoyVttl.TabTAPI_PortTypeClient();
                var frenoyTeams = frenoy.GetClubTeams(new GetClubTeamsRequest
                {
                    Club = "OVL134",
                    Season = "16"
                });

                var reeksRegex = new Regex(@"Afdeling (\d+)(\w+)");
                foreach (var frenoyTeam in frenoyTeams.TeamEntries)
                {
                    var reeks = new Reeks();
                    reeks.Competitie = "VTTL"; // Sporta
                    reeks.ReeksType = "Prov"; // Sporta: Afd
                    reeks.Jaar = 2015;

                    var reeksMatch = reeksRegex.Match(frenoyTeam.DivisionName);
                    reeks.ReeksNummer = reeksMatch.Groups[1].Value;
                    reeks.ReeksCode = reeksMatch.Groups[2].Value;
                    reeks.LinkID = $"{frenoyTeam.DivisionId}_{frenoyTeam.Team}";

                    reeks.FrenoyDivisionId = int.Parse(frenoyTeam.DivisionId);
                    reeks.FrenoyTeamId = frenoyTeam.TeamId;

                    db.Reeksen.Add(reeks);
                    db.SaveChanges();

                    var frenoyDivision = frenoy.GetDivisionRanking(new GetDivisionRankingRequest
                    {
                        DivisionId = frenoyTeam.DivisionId
                    });

                    
                }
            }
        }
    }
}
