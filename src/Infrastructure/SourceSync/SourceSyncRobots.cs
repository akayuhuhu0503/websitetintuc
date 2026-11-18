using System.Text;
using System.Text.RegularExpressions;
using LandPortal.MigrationCli;

namespace LandPortal.Infrastructure;

// Bounded path rules for the existing client's User-Agent. Other agents' rules
// cannot inadvertently grant us access. Restrictions are checked before fetch.
public sealed class SourceSyncRobots
{
    readonly List<(string Agent,string Path,bool Allow)> rules=[];
    public SourceSyncRobots(string text)
    {
        var agents=new List<string>();var hasRules=false;
        foreach(var raw in text.Split('\n'))
        {
            var line=raw.Split('#',2)[0].Trim();var colon=line.IndexOf(':');if(colon<0)continue;
            var field=line[..colon].Trim();var value=line[(colon+1)..].Trim();
            if(field.Equals("User-agent",StringComparison.OrdinalIgnoreCase))
            {if(hasRules){agents.Clear();hasRules=false;}agents.Add(value);}
            else if(field.Equals("Disallow",StringComparison.OrdinalIgnoreCase)||field.Equals("Allow",StringComparison.OrdinalIgnoreCase))
            {
                hasRules=true;
                if(value.Length>0)foreach(var agent in agents)rules.Add((agent,value,field.Equals("Allow",StringComparison.OrdinalIgnoreCase)));
            }
        }
    }
    public bool Allows(string url)
    {
        var path=new Uri(url).PathAndQuery;
        var specific=rules.Any(x=>x.Agent.Equals("LandPortalMigration",StringComparison.OrdinalIgnoreCase));
        var matched=rules.Where(x=>specific?x.Agent.Equals("LandPortalMigration",StringComparison.OrdinalIgnoreCase):x.Agent=="*")
            .Where(x=>Regex.IsMatch(path,"^"+Regex.Escape(x.Path.TrimEnd('$')).Replace("\\*",".*")+(x.Path.EndsWith('$')?"$":""),RegexOptions.None,TimeSpan.FromMilliseconds(50)))
            .OrderByDescending(x=>x.Path.Replace("*","").TrimEnd('$').Length).ThenByDescending(x=>x.Allow).ToList();
        return matched.Count==0||matched[0].Allow;
    }
    public sealed class Session(IQlddSourceSession inner,SourceSyncRobots rules):IQlddSourceSession
    {
        public int RequestsUsed=>inner.RequestsUsed;
        public void Dispose(){} // Caller owns inner.
        public Task<SourceResponse> FetchAsync(string url,int maximum,bool page,CancellationToken ct)
        {
            if(new Uri(url).Host=="qldd.mae.gov.vn"&&!rules.Allows(url))throw new InvalidDataException("Đường dẫn bị giới hạn bởi robots.txt của nguồn.");
            return inner.FetchAsync(url,maximum,page,ct);
        }
    }
}
