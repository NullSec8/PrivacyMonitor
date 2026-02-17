using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PrivacyMonitor
{
    public static class ReportGenerator
    {
        public static string GenerateHtml(ScanResult scan, List<DataFlowEdge>? dataFlowEdges = null, Dictionary<string, HashSet<string>>? identifierToDomains = null)
        {
            var sb = new StringBuilder();
            var score = scan.Score;
            int totalReqs = scan.Requests.Count;
            int thirdParty = scan.Requests.Count(r => r.IsThirdParty);
            int trackers = scan.Requests.Count(r => !string.IsNullOrEmpty(r.TrackerLabel));
            int fps = scan.Fingerprints.Count;
            int trackingCookies = scan.Cookies.Count(c => c.Classification == "Tracking / Analytics");

            sb.Append($@"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""UTF-8"">
<title>Privacy Audit Report — {Esc(scan.TargetUrl)}</title>
<style>
  :root {{ --navy:#0B1929; --blue:#0C4A90; --gold:#C9A24A; --red:#DC2626; --green:#12B76A; --warn:#F79009; --bg:#F0F2F5; --text:#1D2939; --muted:#667085; --border:#E4E7EC; }}
  * {{ margin:0; padding:0; box-sizing:border-box; }}
  body {{ font-family:'Segoe UI',system-ui,sans-serif; background:#fff; color:var(--text); line-height:1.55; font-size:12px; }}
  .page {{ max-width:880px; margin:0 auto; padding:36px 44px; }}
  .header {{ border-bottom:3px solid var(--navy); padding-bottom:18px; margin-bottom:28px; display:flex; justify-content:space-between; align-items:flex-start; }}
  .brand {{ }}
  .brand h1 {{ color:var(--navy); font-size:16px; letter-spacing:0.3px; margin-bottom:2px; }}
  .brand h2 {{ color:var(--muted); font-size:12px; font-weight:400; }}
  .doc-info {{ text-align:right; font-size:11px; color:var(--muted); line-height:1.7; }}
  .doc-info strong {{ color:var(--navy); }}
  .classification {{ display:inline-block; border:2px solid var(--navy); padding:3px 12px; font-size:10px; font-weight:700; letter-spacing:1px; color:var(--navy); margin-bottom:8px; }}
  h3 {{ color:var(--navy); font-size:14px; margin:24px 0 10px; padding-bottom:5px; border-bottom:1px solid var(--border); }}
  .score-box {{ display:flex; align-items:center; gap:20px; background:var(--bg); border-radius:10px; padding:20px; margin-bottom:20px; border:1px solid var(--border); }}
  .grade {{ width:70px; height:70px; border-radius:50%; display:flex; align-items:center; justify-content:center; font-size:34px; font-weight:700; color:#fff; }}
  .score-detail {{ flex:1; }}
  .score-detail .num {{ font-size:26px; font-weight:700; color:var(--text); }}
  .score-detail .summary {{ font-size:12px; color:var(--muted); margin-top:3px; }}
  .stat-grid {{ display:grid; grid-template-columns:repeat(4,1fr); gap:10px; margin-bottom:20px; }}
  .stat {{ background:var(--bg); border-radius:8px; padding:14px; text-align:center; border:1px solid var(--border); }}
  .stat .num {{ font-size:22px; font-weight:700; }}
  .stat .label {{ font-size:10px; color:var(--muted); margin-top:3px; }}
  table {{ width:100%; border-collapse:collapse; margin-bottom:14px; font-size:11px; }}
  th {{ background:var(--navy); color:#fff; padding:7px 9px; text-align:left; font-size:10px; text-transform:uppercase; letter-spacing:0.5px; }}
  td {{ padding:6px 9px; border-bottom:1px solid var(--border); vertical-align:top; }}
  tr:nth-child(even) {{ background:#FAFBFD; }}
  .sev-critical {{ color:var(--red); font-weight:700; }}
  .sev-high {{ color:#D97706; font-weight:700; }}
  .sev-medium {{ color:#92600A; }}
  .sev-low {{ color:var(--muted); }}
  .st-pass {{ color:var(--green); font-weight:700; }}
  .st-fail {{ color:var(--red); font-weight:700; }}
  .st-warn {{ color:var(--warn); font-weight:700; }}
  .tag {{ display:inline-block; padding:1px 7px; border-radius:4px; font-size:9px; font-weight:600; }}
  .tag-tracker {{ background:#FEE2E2; color:var(--red); }}
  .tag-3rd {{ background:#FEF3C7; color:#92600A; }}
  .tag-1st {{ background:#D1FAE5; color:#065F46; }}
  .breakdown {{ font-size:11px; color:var(--muted); margin-top:6px; }}
  .breakdown span {{ margin-right:14px; }}
  .gdpr-desc {{ font-size:10px; color:#555; max-width:380px; }}
  .footer {{ margin-top:36px; padding-top:16px; border-top:2px solid var(--navy); font-size:10px; color:var(--muted); line-height:1.6; }}
  @media print {{ .page {{ padding:18px; }} body {{ font-size:10px; }} }}
</style>
</head>
<body>
<div class=""page"">

<div class=""header"">
  <div class=""brand"">
    <div class=""classification"">OFFICIAL</div>
    <h1>AGJENCIA PER INFORMIM DHE PRIVATESI</h1>
    <h2>Agency for Information and Privacy &mdash; Privacy Audit Report</h2>
  </div>
  <div class=""doc-info"">
    <strong>Target:</strong> {Esc(scan.TargetUrl)}<br>
    <strong>Date:</strong> {scan.ScanStart.ToLocalTime():yyyy-MM-dd HH:mm:ss}<br>
    <strong>Duration:</strong> {(scan.ScanEnd - scan.ScanStart).TotalSeconds:F1}s<br>
    <strong>Report ID:</strong> {Guid.NewGuid().ToString()[..8].ToUpper()}
  </div>
</div>

<div class=""score-box"">
  <div class=""grade"" style=""background:{ScoreColor(score.NumericScore)}"">{Esc(score.Grade)}</div>
  <div class=""score-detail"">
    <div class=""num"">{score.NumericScore} / 100</div>
    <div class=""summary"">{Esc(score.Summary)}<br><em>{Esc(score.SummarySq)}</em></div>
    <div class=""breakdown"">");

            foreach (var kv in score.Breakdown.Where(b => b.Value != 0))
                sb.Append($"<span>{Esc(kv.Key)}: {kv.Value}</span>");

            sb.Append($@"</div>
  </div>
</div>

<div class=""stat-grid"">
  <div class=""stat""><div class=""num"" style=""color:var(--navy)"">{totalReqs}</div><div class=""label"">Total Requests</div></div>
  <div class=""stat""><div class=""num"" style=""color:{(thirdParty > 10 ? "#D97706" : "var(--navy)")}"">{thirdParty}</div><div class=""label"">Third-Party</div></div>
  <div class=""stat""><div class=""num"" style=""color:{(trackers > 0 ? "var(--red)" : "var(--green)")}"">{trackers}</div><div class=""label"">Trackers</div></div>
  <div class=""stat""><div class=""num"" style=""color:{(fps > 0 ? "var(--red)" : "var(--green)")}"">{fps}</div><div class=""label"">Fingerprinting</div></div>
</div>
");

            // GDPR
            if (scan.GdprFindings.Count > 0)
            {
                sb.Append(@"<h3>GDPR Compliance Findings</h3><table><tr><th>Article</th><th>Title</th><th>Severity</th><th>Count</th><th>Description</th></tr>");
                foreach (var g in scan.GdprFindings.OrderByDescending(f => f.Severity))
                {
                    string sc = g.Severity.ToLower() switch { "critical" => "sev-critical", "high" => "sev-high", "medium" => "sev-medium", _ => "sev-low" };
                    sb.Append($@"<tr><td><strong>{Esc(g.Article)}</strong></td><td>{Esc(g.Title)}</td><td class=""{sc}"">{Esc(g.Severity)}</td><td>{g.Count}</td><td class=""gdpr-desc"">{Esc(g.Description)}</td></tr>");
                }
                sb.Append("</table>");
            }

            // Fingerprinting
            if (scan.Fingerprints.Count > 0)
            {
                sb.Append(@"<h3>Fingerprinting Techniques Detected</h3><table><tr><th>Technique</th><th>Detail</th><th>Severity</th><th>GDPR</th></tr>");
                foreach (var fp in scan.Fingerprints)
                    sb.Append($@"<tr><td><strong>{Esc(fp.Type)}</strong></td><td>{Esc(fp.Detail)}</td><td class=""sev-high"">{Esc(fp.Severity)}</td><td>{Esc(fp.GdprArticle)}</td></tr>");
                sb.Append("</table>");
            }

            // Security Headers
            sb.Append(@"<h3>Security Headers Audit</h3><table><tr><th>Header</th><th>Status</th><th>Value</th><th>Purpose</th></tr>");
            foreach (var sh in scan.SecurityHeaders)
            {
                string sc = sh.Status switch { "Present" => "st-pass", "Weak" => "st-warn", _ => "st-fail" };
                string label = sh.Status switch { "Present" => "PASS", "Weak" => "WARN", _ => "FAIL" };
                sb.Append($@"<tr><td><strong>{Esc(sh.Header)}</strong></td><td class=""{sc}"">{label}</td><td style=""font-size:9px;word-break:break-all;max-width:240px"">{Esc(sh.Value)}</td><td style=""font-size:10px"">{Esc(sh.Explanation)}</td></tr>");
            }
            sb.Append("</table>");

            // Credential safety + suspicious signals
            var susp = SecurityHeuristics.AssessSuspicion(scan);
            var cred = SecurityHeuristics.AssessCredentialSafety(scan);

            string credColor = cred.Level switch
            {
                CredentialSafety.Safe => "var(--green)",
                CredentialSafety.Caution => "var(--warn)",
                _ => "var(--red)"
            };

            sb.Append($@"<h3>Credential Safety Hint</h3>
<p style=""font-size:11px;""><strong style=""color:{credColor}"">{Esc(cred.Level.ToString())}</strong> &mdash; {Esc(cred.Explanation)}</p>");

            if (susp.Reasons.Count > 0)
            {
                string suspColor = susp.Level switch
                {
                    SuspicionLevel.Low => "var(--muted)",
                    SuspicionLevel.Medium => "var(--warn)",
                    _ => "var(--red)"
                };
                sb.Append($@"<h3>Suspicious Signals</h3>
<p style=""font-size:11px;color:{suspColor}"">Overall suspicion: {Esc(susp.Level.ToString())}</p>
<ul style=""font-size:11px;color:#4B5563;margin-left:16px;margin-bottom:10px"">");
                foreach (var r in susp.Reasons)
                    sb.Append($@"<li>{Esc(r)}</li>");
                sb.Append("</ul>");
            }

            // Cookies
            if (scan.Cookies.Count > 0)
            {
                sb.Append(@"<h3>Cookies</h3><table><tr><th>Name</th><th>Classification</th><th>Third-Party</th></tr>");
                foreach (var c in scan.Cookies.Take(50))
                    sb.Append($@"<tr><td>{Esc(c.Name)}</td><td>{Esc(c.Classification)}</td><td>{(c.IsThirdParty ? "<span class='tag tag-3rd'>3rd</span>" : "1st")}</td></tr>");
                if (scan.Cookies.Count > 50) sb.Append($"<tr><td colspan='3'><em>and {scan.Cookies.Count - 50} more</em></td></tr>");
                sb.Append("</table>");
            }

            // Storage
            if (scan.Storage.Count > 0)
            {
                sb.Append(@"<h3>Web Storage</h3><table><tr><th>Store</th><th>Key</th><th>Size</th><th>Classification</th></tr>");
                foreach (var s in scan.Storage.Take(40))
                    sb.Append($@"<tr><td>{Esc(s.Store)}</td><td>{Esc(s.Key)}</td><td>{s.Size:N0} ch</td><td>{Esc(s.Classification)}</td></tr>");
                sb.Append("</table>");
            }

            // WebRTC
            if (scan.WebRtcLeaks.Count > 0)
            {
                sb.Append(@"<h3>WebRTC IP Leaks</h3><table><tr><th>IP Address</th><th>Type</th><th>Risk</th></tr>");
                foreach (var l in scan.WebRtcLeaks)
                    sb.Append($@"<tr><td><strong>{Esc(l.IpAddress)}</strong></td><td>{Esc(l.Type)}</td><td class=""sev-high"">Real location exposed</td></tr>");
                sb.Append("</table>");
            }

            // Top trackers
            var topTrackers = scan.Requests.Where(r => !string.IsNullOrEmpty(r.TrackerLabel)).GroupBy(r => r.TrackerLabel).OrderByDescending(g => g.Count()).Take(15).ToList();
            if (topTrackers.Count > 0)
            {
                sb.Append(@"<h3>Top Trackers</h3><table><tr><th>Tracker</th><th>Requests</th><th>Sample Host</th></tr>");
                foreach (var g in topTrackers)
                    sb.Append($@"<tr><td><strong>{Esc(g.Key)}</strong></td><td>{g.Count()}</td><td style=""font-size:10px"">{Esc(g.First().Host)}</td></tr>");
                sb.Append("</table>");
            }

            // Script inventory (for analyst / educational use)
            var scripts = ScriptCatalog.Build(scan);
            if (scripts.Count > 0)
            {
                sb.Append(@"<h3>Script &amp; Source Inventory</h3><table><tr><th>Host</th><th>Path</th><th>Scope</th><th>Tracker</th></tr>");
                foreach (var s in scripts)
                {
                    string scope = s.IsThirdParty ? "<span class='tag tag-3rd'>3rd-party</span>" : "<span class='tag tag-1st'>This site</span>";
                    string tracker = s.IsTracker ? $"<span class='tag tag-tracker'>{Esc(s.TrackerLabel)}</span>" : "";
                    sb.Append($@"<tr><td>{Esc(s.Host)}</td><td style=""font-size:10px"">{Esc(s.Path)}</td><td>{scope}</td><td>{tracker}</td></tr>");
                }
                sb.Append("</table>");
            }

            // ═══════════ FORENSIC ANALYSIS ═══════════

            // Company clusters
            var clusters = ForensicEngine.ClusterByCompany(scan.Requests);
            if (clusters.Count > 0)
            {
                sb.Append(@"<h3>Company Data Flow Analysis</h3><table><tr><th>Company</th><th>Services</th><th>Requests</th><th>Categories</th><th>Data Types</th></tr>");
                foreach (var c in clusters.Take(15))
                    sb.Append($@"<tr><td><strong>{Esc(c.Company)}</strong></td><td style=""font-size:10px"">{Esc(string.Join(", ", c.Services.Take(4)))}</td><td>{c.TotalRequests}</td><td style=""font-size:10px"">{Esc(string.Join(", ", c.Categories))}</td><td style=""font-size:9px"">{Esc(string.Join(", ", c.DataTypes.Take(5)))}</td></tr>");
                sb.Append("</table>");
            }

            // Identity links
            if (identifierToDomains != null)
            {
                var links = ForensicEngine.BuildIdentityLinks(identifierToDomains);
                if (links.Count > 0)
                {
                    sb.Append(@"<h3>Identity Stitching Detection</h3><p style=""font-size:11px;color:#667085;margin-bottom:8px"">Shared identifiers detected across multiple domains, indicating cross-site user tracking.</p><table><tr><th>Parameter</th><th>Domains</th><th>Risk</th><th>Confidence</th></tr>");
                    foreach (var l in links)
                        sb.Append($@"<tr><td><strong>{Esc(l.ParameterName)}</strong></td><td style=""font-size:10px"">{Esc(string.Join(", ", l.Domains))}</td><td class=""sev-{l.RiskLevel.ToLower()}"">{Esc(l.RiskLevel)}</td><td>{l.Confidence:P0}</td></tr>");
                    sb.Append("</table>");
                }
            }

            // Data flow edges
            if (dataFlowEdges != null && dataFlowEdges.Count > 0)
            {
                sb.Append(@"<h3>Data Flow Graph</h3><p style=""font-size:11px;color:#667085;margin-bottom:8px"">Observed data transfers between domains during the browsing session.</p><table><tr><th>From</th><th>To</th><th>Mechanism</th><th>Occurrences</th></tr>");
                foreach (var e in dataFlowEdges.OrderByDescending(x => x.Occurrences).Take(25))
                    sb.Append($@"<tr><td>{Esc(e.FromDomain)}</td><td>{Esc(e.ToDomain)}</td><td><span class=""tag tag-3rd"">{Esc(e.Mechanism)}</span></td><td>{e.Occurrences}</td></tr>");
                sb.Append("</table>");
            }

            // Behavioral patterns
            var patterns = ForensicEngine.DetectBehavioralPatterns(scan.Fingerprints);
            if (patterns.Count > 0)
            {
                sb.Append(@"<h3>Behavioral Fingerprinting Patterns</h3><table><tr><th>Pattern</th><th>Detail</th><th>Confidence</th><th>Severity</th></tr>");
                foreach (var p in patterns)
                    sb.Append($@"<tr><td><strong>{Esc(p.Name)}</strong></td><td style=""font-size:10px"">{Esc(p.Detail)}</td><td>{p.Confidence:P0}</td><td class=""sev-critical"">{p.Severity}/5</td></tr>");
                sb.Append("</table>");
            }

            // Score explanations
            var explanations = ForensicEngine.ExplainScore(scan.Score, scan, clusters);
            if (explanations.Count > 0)
            {
                sb.Append(@"<h3>Score Justification (Regulatory-Defensible)</h3><table><tr><th>Category</th><th>Penalty</th><th>GDPR</th><th>Justification</th></tr>");
                foreach (var ex in explanations)
                    sb.Append($@"<tr><td><strong>{Esc(ex.Category)}</strong></td><td class=""sev-high"">{ex.Penalty}</td><td>{Esc(ex.GdprRelevance)}</td><td class=""gdpr-desc"">{Esc(ex.Justification)}</td></tr>");
                sb.Append("</table>");
            }

            // Educational lessons
            var lessons = LessonEngine.BuildLessons(scan);
            if (lessons.Count > 0)
            {
                sb.Append(@"<h3>Guided Lessons for This Site</h3><table><tr><th>Topic</th><th>What it is</th><th>Why it matters</th><th>Where to look</th></tr>");
                foreach (var l in lessons)
                {
                    sb.Append($@"<tr><td><strong>{Esc(l.Title)}</strong></td><td class=""gdpr-desc"">{Esc(l.What)}</td><td class=""gdpr-desc"">{Esc(l.WhyItMatters)}</td><td style=""font-size:10px"">{Esc(l.WhereToLook)}</td></tr>");
                }
                sb.Append("</table>");
            }

            // Session risk
            var sessionRisk = ForensicEngine.AssessSessionRisk(scan,
                identifierToDomains != null ? ForensicEngine.BuildIdentityLinks(identifierToDomains) : new(),
                patterns, dataFlowEdges ?? new(), clusters);
            sb.Append($@"
<h3>Session Risk Assessment</h3>
<div class=""stat-grid"">
  <div class=""stat""><div class=""num"" style=""color:{RiskColor(sessionRisk.IdentityStitchingRisk)}"">{Esc(sessionRisk.IdentityStitchingRisk)}</div><div class=""label"">Identity Stitching</div></div>
  <div class=""stat""><div class=""num"" style=""color:{RiskColor(sessionRisk.DataPropagationRisk)}"">{Esc(sessionRisk.DataPropagationRisk)}</div><div class=""label"">Data Propagation</div></div>
  <div class=""stat""><div class=""num"" style=""color:{RiskColor(sessionRisk.FingerprintingRisk)}"">{Esc(sessionRisk.FingerprintingRisk)}</div><div class=""label"">Fingerprinting</div></div>
  <div class=""stat""><div class=""num"" style=""color:{RiskColor(sessionRisk.ConcentrationRisk)}"">{Esc(sessionRisk.ConcentrationRisk)}</div><div class=""label"">Concentration</div></div>
</div>
<p style=""font-size:11px;color:#667085"">{Esc(sessionRisk.Summary)}</p>");

            sb.Append($@"
<div class=""footer"">
  <strong>AGJENCIA PER INFORMIM DHE PRIVATESI</strong> &mdash; Agency for Information and Privacy<br>
  Privacy Audit Report generated by Privacy Monitor on {DateTime.Now:yyyy-MM-dd HH:mm:ss}<br>
  This report is generated automatically. Findings should be verified by a qualified data protection officer.<br>
  <em>Ky raport gjenerohet automatikisht. Gjetjet duhet te verifikohen nga nje oficer i kualifikuar i mbrojtjes se te dhenave.</em>
</div>
</div></body></html>");

            return sb.ToString();
        }

        private static string Esc(string s) => System.Net.WebUtility.HtmlEncode(s ?? "");

        private static string ScoreColor(int score) =>
            score >= 90 ? "#065F46" : score >= 75 ? "#0C4A90" : score >= 60 ? "#92600A" : score >= 40 ? "#9A3412" : "#7F1D1D";

        private static string RiskColor(string risk) => risk switch
        {
            "Critical" => "var(--red)",
            "High" => "#D97706",
            "Medium" => "#92600A",
            _ => "var(--green)"
        };
    }
}
