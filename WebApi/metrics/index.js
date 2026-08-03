/* Admin metrics API for www.jah.cc/admin (ANALYTICS_PLAN.md).
   Named queries only — the client never sends KQL. SWA's route rules already
   restrict /api/* to the admin role; the principal check below is the second
   lock on the same door in case a route edit ever drops the first. */

const QUERIES = {
  summary:
    "let ge = customEvents | where tostring(customDimensions.env) == '__ENV__' and cloud_RoleName == 'jah';" +
    "let se = customEvents | where cloud_RoleName == 'site';" +
    "print PlayersToday = toscalar(ge | where timestamp > ago(1d) | summarize dcount(user_Id))," +
    " Players7d = toscalar(ge | where timestamp > ago(7d) | summarize dcount(user_Id))," +
    " Sessions7d = toscalar(ge | where timestamp > ago(7d) | summarize dcount(session_Id))," +
    " Matches7d = toscalar(ge | where name == 'match_start' and timestamp > ago(7d) | summarize count())," +
    " SiteViews7d = toscalar(se | where name == 'page_view' and timestamp > ago(7d) | summarize count())",
  dau:
    "customEvents | where tostring(customDimensions.env) == '__ENV__' and cloud_RoleName == 'jah' and timestamp > ago(14d)" +
    " | summarize Players = dcount(user_Id) by Day = bin(timestamp, 1d) | order by Day asc",
  modes:
    "customEvents | where name == 'match_start' and tostring(customDimensions.env) == '__ENV__' and timestamp > ago(14d)" +
    " | summarize Matches = count() by Mode = tostring(customDimensions.mode) | order by Matches desc",
  robots:
    "customEvents | where name == 'robot_select' and tostring(customDimensions.env) == '__ENV__' and timestamp > ago(14d)" +
    " | summarize Picks = count() by Robot = tostring(customDimensions.cyan) | order by Picks desc",
  funnel:
    "customEvents | where tostring(customDimensions.env) == '__ENV__' and cloud_RoleName == 'jah' and timestamp > ago(7d)" +
    " | where name in ('session_start', 'menu_view', 'mode_select', 'match_start', 'match_end')" +
    " | summarize Count = count() by Step = name" +
    " | extend Order = case(Step == 'session_start', 1, Step == 'menu_view', 2, Step == 'mode_select', 3, Step == 'match_start', 4, 5)" +
    " | order by Order asc | project Step, Count",
  duration:
    "customEvents | where name == 'match_end' and tostring(customDimensions.env) == '__ENV__' and timestamp > ago(14d)" +
    " | summarize Minutes = round(percentile(todouble(customMeasurements.duration_s), 50) / 60, 1) by Mode = tostring(customDimensions.mode)" +
    " | order by Minutes desc",
  boot:
    "customEvents | where name == 'boot_perf' and tostring(customDimensions.env) == '__ENV__' and timestamp > ago(14d)" +
    " | summarize p50 = round(percentile(todouble(customMeasurements.load_ms), 50)), p90 = round(percentile(todouble(customMeasurements.load_ms), 90)) by Day = bin(timestamp, 1d)" +
    " | order by Day asc",
  site:
    "customEvents | where cloud_RoleName == 'site' and timestamp > ago(14d)" +
    " | summarize Views = countif(name == 'page_view'), Plays = countif(name == 'play_click') by Day = bin(timestamp, 1d)" +
    " | order by Day asc",
  errors:
    "customEvents | where name == 'error' and tostring(customDimensions.env) == '__ENV__' and timestamp > ago(7d)" +
    " | summarize Hits = count(), Players = dcount(user_Id) by Message = tostring(customDimensions.msg), At = tostring(customDimensions.where)" +
    " | top 10 by Hits"
};

function userRoles(req) {
  try {
    const b64 = req.headers['x-ms-client-principal'];
    if (!b64) return [];
    const principal = JSON.parse(Buffer.from(b64, 'base64').toString('utf8'));
    return principal.userRoles || [];
  } catch (e) { return []; }
}

module.exports = async function (context, req) {
  if (!userRoles(req).includes('admin')) {
    context.res = { status: 403, body: { error: 'admin role required' } };
    return;
  }
  const name = String(context.bindingData.name || '').toLowerCase();
  const query = QUERIES[name];
  if (!query) {
    context.res = { status: 404, body: { error: 'unknown metric' } };
    return;
  }
  // env is a two-value whitelist substituted into a quoted literal — no
  // client-supplied text ever reaches the KQL.
  const env = req.query.env === 'dev' ? 'dev' : 'prod';
  const kql = query.replace(/__ENV__/g, env);

  const appId = process.env.APPINSIGHTS_APPID;
  const apiKey = process.env.APPINSIGHTS_APIKEY;
  if (!appId || !apiKey) {
    context.res = { status: 500, body: { error: 'api not configured' } };
    return;
  }
  try {
    const r = await fetch(
      'https://api.applicationinsights.io/v1/apps/' + appId + '/query?query=' + encodeURIComponent(kql),
      { headers: { 'x-api-key': apiKey } });
    if (!r.ok) {
      context.res = { status: 502, body: { error: 'query failed (' + r.status + ')' } };
      return;
    }
    const data = await r.json();
    const table = (data.tables && data.tables[0]) || { columns: [], rows: [] };
    context.res = {
      status: 200,
      headers: { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' },
      body: { columns: table.columns.map(c => c.name), rows: table.rows }
    };
  } catch (e) {
    context.res = { status: 502, body: { error: 'query failed' } };
  }
};
