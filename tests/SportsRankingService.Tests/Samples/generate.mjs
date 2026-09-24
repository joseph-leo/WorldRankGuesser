// Writes one synthetic response per feed into this folder. Everything here is invented except country
// codes and names; athlete surnames are the NATO alphabet. Keep each file to the shape its parser reads.
// Run: node tests/SportsRankingService.Tests/Samples/generate.mjs
import { writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const write = (name, text) => writeFileSync(join(here, name), text.endsWith('\n') ? text : text + '\n');
const json = (value) => JSON.stringify(value, null, 2);

const NAMES = ['Jonah ALPHA', 'Mika BRAVO', 'Sacha CHARLIE', 'Rene DELTA', 'Toni ECHO', 'Kim FOXTROT', 'Ari GOLF', 'Lou HOTEL',
  'Sam INDIA', 'Noa JULIET', 'Eli KILO', 'Max LIMA', 'Ola MIKE', 'Uma NOVEMBER', 'Ivo OSCAR', 'Zoe PAPA', 'Rae QUEBEC', 'Ash ROMEO', 'Ida SIERRA', 'Bo TANGO'];
const name = (i) => NAMES[i % NAMES.length];
const given = (i) => name(i).split(' ')[0];
const surname = (i) => name(i).split(' ')[1];
const capitalized = (i) => `${surname(i)[0]}${surname(i).slice(1).toLowerCase()}`;   // "Alpha"
const titleCase = (i) => `${given(i)} ${capitalized(i)}`;                              // "Jonah Alpha"

// ---- BWF: Laravel envelope, countries as names, names as HTML ----------------------------------------------
const bwfName = (i) => `<span class="name-1">${given(i)}</span> <span class="name-2">${surname(i)}</span>`;
const bwfRow = (id, rank, points, p1, p2 = null) => ({
  id, rank, points: `${points}.0000`,
  player1_id: p1.id, player2_id: p2 ? p2.id : null,
  player1_model: { slug: `player-${p1.id}`, name_display_bold: bwfName(p1.n) },
  player2_model: p2 ? { slug: `player-${p2.id}`, name_display_bold: bwfName(p2.n) } : null,
  p1_country_model: { name: p1.country }, p2_country_model: p2 ? { name: p2.country } : null,
});
const bwf = (rows) => json({ results: { data: rows, total: rows.length, per_page: rows.length, last_page: 1 } });

const singlesCountries = ['INDONESIA', 'DENMARK', 'CHINA', 'JAPAN', 'THAILAND', 'MALAYSIA', 'INDIA', 'FRANCE', 'SINGAPORE', 'Korea', 'HONG KONG CHINA', 'AZERBAIJAN'];
write('Bwf_MensSingles.json', bwf(singlesCountries.map((country, i) =>
  bwfRow(100 + i, i + 1, 87631 - i * 3000, { id: 1000 + i, n: i, country }))));

const doublesPairs = [
  ['Korea', 'Korea'], ['DENMARK', 'DENMARK'], ['MALAYSIA', 'MALAYSIA'], ['INDONESIA', 'INDONESIA'], ['Korea', 'MALAYSIA'],
  ['JAPAN', 'JAPAN'], ['CHINA', 'CHINA'], ['Athlete Independent Neutral', 'Athlete Independent Neutral'], ['INDIA', 'INDIA'], ['FRANCE', 'FRANCE'],
];
write('Bwf_MensDoubles.json', bwf(doublesPairs.map(([c1, c2], i) =>
  bwfRow(200 + i, i + 1, 114099 - i * 4000, { id: 2000 + 2 * i, n: 2 * i, country: c1 }, { id: 2001 + 2 * i, n: 2 * i + 1, country: c2 }))));

// Five consecutive rows tied at rank 925: three players, two of them listed twice (a new row id, the same player id).
write('Bwf_MensSingles_RepeatedRows.json', bwf([
  bwfRow(901, 925, 300, { id: 51, n: 0, country: 'PERU' }),
  bwfRow(902, 925, 300, { id: 51, n: 0, country: 'PERU' }),
  bwfRow(903, 925, 300, { id: 52, n: 1, country: 'DOMINICAN REPUBLIC' }),
  bwfRow(904, 925, 300, { id: 53, n: 2, country: 'CANADA' }),
  bwfRow(905, 925, 300, { id: 53, n: 2, country: 'CANADA' }),
]));

// One player (id 7) in two pairs tied on rank and points.
write('Bwf_MensDoubles_TiedPairs.json', bwf([
  bwfRow(501, 505, 2200, { id: 7, n: 8, country: 'USA' }, { id: 8, n: 9, country: 'USA' }),
  bwfRow(502, 505, 2200, { id: 7, n: 8, country: 'USA' }, { id: 9, n: 10, country: 'USA' }),
]));

// ---- ESPN tennis ---------------------------------------------------------------------------------------------
const espnCountries = ['ITA', 'ESP', 'GER', 'SUI', 'USA', 'RUS', 'DNK', 'NOR', 'AUS', 'GRC', 'CAN', 'LUX'];
write('Espn_Atp_Singles.json', json({
  rankings: [{
    name: 'ATP Singles', update: '2026-09-10T06:00:00Z',
    ranks: espnCountries.map((c, i) => ({ current: i + 1, previous: i + 1, points: 11500 - i * 700, athlete: { citizenshipCountry: c, displayName: titleCase(i) } })),
  }],
}));

// ---- FIBA page: first table is the ranking; later tables are movers widgets; a date dropdown ------------------
const fibaTeams = [['usa', 'United States', 952.3], ['germany', 'Germany', 840.1], ['france', 'France', 811.4], ['serbia', 'Serbia', 780.0],
  ['canada', 'Canada', 760.9], ['australia', 'Australia', 731.2], ['lithuania', 'Lithuania', 700.5], ['spain', 'Spain', 690.0],
  ['slovenia', 'Slovenia', 640.3], ['new-zealand', 'New Zealand', 601.7], ['brazil', 'Brazil', 590.2], ['greece', 'Greece', 571.8]];
const fibaRow = ([slug, label, pts], i) =>
  `      <tr><td>${i + 1}.</td><td><a href="/fr/teams/${150 + i}-${slug}">${label}</a></td><td>${i + 1}.</td><td>${pts.toFixed(1)}</td><td>0</td></tr>`;
const fibaWidget = (title, picks) => `    <h2>${title}</h2>
    <table>
      <tbody>
${picks.map(([slug, label], i) => `      <tr><td>${i + 1}</td><td><a href="/fr/teams/${150 + i}-${slug}">${label}</a></td><td>+${3 - i}</td></tr>`).join('\n')}
      </tbody>
    </table>`;
write('Fiba_Ranking_Men.html', `<!DOCTYPE html>
<html lang="fr">
<head><meta charset="utf-8"><title>Sample ranking page</title></head>
<body>
  <main>
    <select name="rankingDatesselect">
      <option value="2026-09-01T00:00:00.000Z" selected="">1 Sep 2026</option>
      <option value="2026-07-13T00:00:00.000Z">13 Jul 2026</option>
      <option value="2026-03-03T00:00:00.000Z">3 Mar 2026</option>
    </select>
    <table>
      <thead><tr><th>#</th><th>Country</th><th>Zone rank</th><th>PTS</th><th>+/-</th></tr></thead>
      <tbody>
${fibaTeams.map(fibaRow).join('\n')}
      </tbody>
    </table>
${fibaWidget('Biggest movers', fibaTeams.slice(9, 12))}
${fibaWidget('Biggest drops', fibaTeams.slice(3, 6))}
  </main>
</body>
</html>`);

// ---- FIFA v3 API and the two pages with the ranking-schedule ids -------------------------------------------
const fifa = (rows) => json({ Results: rows.map(([code, rank, pts, label]) => ({ Rank: rank, IdCountry: code, Name: label, TotalPoints: pts })) });
write('Fifa_V3_Men.json', fifa([['ESP', 1, 1995.881879, 'Spain'], ['ARG', 2, 1880.2, 'Argentina'], ['FRA', 3, 1870.5, 'France'], ['ENG', 4, 1820.0, 'England'],
  ['BRA', 5, 1790.4, 'Brazil'], ['GER', 6, 1740.1, 'Germany'], ['NED', 7, 1730.9, 'Netherlands'], ['POR', 8, 1725.3, 'Portugal'],
  ['BEL', 9, 1700.0, 'Belgium'], ['ITA', 10, 1690.2, 'Italy'], ['MAR', 11, 1680.7, 'Morocco'], ['SMR', 12, 720.0, 'San Marino']]));
write('Fifa_V3_Women.json', fifa([['ESP', 1, 2066.8, 'Spain'], ['USA', 2, 2050.1, 'USA'], ['SWE', 3, 2010.3, 'Sweden'], ['GER', 4, 2000.0, 'Germany'],
  ['ENG', 5, 1990.6, 'England'], ['FRA', 6, 1960.2, 'France'], ['JPN', 7, 1950.9, 'Japan'], ['MRI', 8, 900.0, 'Mauritius'],
  ['SMR', null, null, 'San Marino'], ['AIA', null, null, 'Anguilla']]));

const fifaPage = (dates) => `<!DOCTYPE html>
<html lang="en">
<head><meta charset="utf-8"><title>Sample FIFA ranking page</title></head>
<body>
  <div id="__next"></div>
  <script id="__NEXT_DATA__" type="application/json">${JSON.stringify({ props: { pageProps: { pageData: { ranking: { dates } } } } })}</script>
</body>
</html>`;
write('Fifa_WorldRanking_Men.html', fifaPage([
  { year: '2026', dates: [{ id: 'FRS_Male_Football_20260611', iso: '2026-07-20T08:37:28.979Z', dateText: '20 July 2026' }, { id: 'FRS_Male_Football_20260327', iso: '2026-04-03T10:00:00.000Z', dateText: '3 April 2026' }] },
  { year: '2025', dates: [{ id: 'id14870', iso: '2025-09-18T00:00:00.000Z', dateText: '18 September 2025' }] },
]));
write('Fifa_WorldRanking_Women.html', fifaPage([
  { year: '2026', dates: [{ id: 'FRS_Female_Football_20260419', iso: '2026-04-24T09:00:00.000Z', dateText: '24 April 2026' }, { id: 'FRS_Female_Football_20260110', iso: '2026-01-15T09:00:00.000Z', dateText: '15 January 2026' }] },
  { year: '2025', dates: [{ id: 'FRS_Female_Football_20251201', iso: '2025-12-05T09:00:00.000Z', dateText: '5 December 2025' }] },
]));

// ---- FIG pages: an h4 per series, a nav-tabs strip per series, a pane per apparatus -------------------------
const FIG_SERIES = [['wc', 'World Cup', 'Apparatus World Cup Ranking List 2026', 6], ['ch', 'World Challenge Cup', 'World Challenge Cup Ranking List 2026', 5]];
const POOL = ['USA', 'JPN', 'GBR', 'CHN', 'ITA', 'BRA', 'FRA', 'CAN', 'AUS', 'ESP', 'KOR', 'GER', 'UKR', 'TUR', 'HUN', 'ROU', 'BEL', 'NED', 'MEX', 'EGY'];
const figName = (i) => `${surname(i)}&nbsp;${given(i)}`;
// rows: [rank, ioc, athleteIndex | null (a national group), total]
const defaultRows = (count, offset) => Array.from({ length: count }, (_, i) => [i + 1, POOL[(offset + i) % POOL.length], offset + i, 60 - i * 4]);
const groupRows = (codes) => codes.map((c, i) => [i + 1, c, null, 60.5 - i * 3]);
const figRow = ([rank, ioc, athlete, total]) => athlete === null
  ? `            <tr><td data-label='Rank' class='bold' align='center'><b>${rank}</b></td><td data-label='NF'><nobr><img src='' alt='${ioc}'>&nbsp;${ioc}</nobr></td><td data-label='NF'>${ioc}</td><td data-label='Total' align='center'>${total}</td></tr>`
  : `            <tr><td data-label='Rank' class='bold' align='center'><b>${rank}</b></td><td data-label='NF'><nobr><img src='' alt='${ioc}'>&nbsp;${ioc}</nobr></td><td data-label='Name'>${figName(athlete)}</td><td data-label='Total' align='center'>${total}</td></tr>`;
const slug = (s) => s.toLowerCase().replace(/[^a-z0-9]+/g, '-');
const figPage = (title, apparatus, special) => {
  let offset = 0;
  const sections = FIG_SERIES.map(([key, series, heading, count]) => {
    const tabs = apparatus.map((a) => `          <li><a data-toggle="tab" href="#${key}_${slug(a)}">${a}</a></li>`).join('\n');
    const panes = apparatus.map((a) => {
      const rows = special[`${series} / ${a}`] ?? defaultRows(count, (offset += 3));
      return `          <div id="${key}_${slug(a)}" class="tab-pane">
            <table class="table">
              <thead><tr><th>Rank</th><th>NF</th><th>Name</th><th>Total</th></tr></thead>
              <tbody>
${rows.map(figRow).join('\n')}
              </tbody>
            </table>
          </div>`;
    }).join('\n');
    return `      <h4>${heading}</h4>
      <div>
        <ul class="nav nav-tabs">
${tabs}
        </ul>
        <div class="tab-content">
${panes}
        </div>
      </div>`;
  }).join('\n');
  return `<!DOCTYPE html>
<html lang="en">
<head><meta charset="utf-8"><title>${title}</title></head>
<body>
  <div class="container">
${sections}
  </div>
</body>
</html>`;
};
write('Fig_Artistic_Men.html', figPage('Sample MAG ranking', ['Floor Exercise', 'Pommel Horse', 'Still Rings', 'Vault', 'Parallel Bars', 'Horizontal Bar'], {
  'World Cup / Floor Exercise': [[1, 'BLR', 0, 71], [2, 'PHI', 1, 66], [3, 'ISR', 2, 60], [4, 'JPN', 3, 55], [5, 'USA', 4, 50], [6, 'GBR', 5, 45]],
  'World Challenge Cup / Floor Exercise': [[1, 'GER', 6, 40], [2, 'TUR', 7, 36], [3, 'CRO', 8, 33], [4, 'ROU', 9, 30], [5, 'HUN', 10, 27]],
  'World Challenge Cup / Horizontal Bar': [[1, 'BUL', 11, 42], [1, 'GER', 12, 42], [3, 'JPN', 13, 38], [4, 'USA', 14, 35], [5, 'CHN', 15, 31]],
}));
write('Fig_Artistic_Women.html', figPage('Sample WAG ranking', ['Vault', 'Uneven Bars', 'Balance Beam', 'Floor Exercise'], {
  'World Cup / Balance Beam': [[1, 'ALG', 1, 70], [2, 'USA', 2, 65], [3, 'BRA', 3, 60], [4, 'CHN', 4, 55], [5, 'ITA', 5, 50], [6, 'CAN', 6, 45]],
}));
write('Fig_Rhythmic_Women.html', figPage('Sample RG ranking', ['Individual All-Around', 'Hoop', 'Ball', 'Clubs', 'Ribbon', 'Group All-Around', 'Group 5x', 'Group 3x+2x'], {
  'World Cup / Group 5x': groupRows(['CHN', 'RUS', 'ITA', 'ISR', 'BUL']),
  'World Cup / Group All-Around': groupRows(['ITA', 'CHN', 'ISR', 'BUL', 'ESP']),
  'World Cup / Group 3x+2x': groupRows(['ISR', 'ITA', 'CHN', 'ESP', 'BRA']),
  'World Challenge Cup / Group All-Around': groupRows(['ESP', 'BRA', 'UKR', 'MEX', 'EGY']),
  'World Challenge Cup / Group 5x': groupRows(['BRA', 'ESP', 'UKR', 'MEX', 'EGY']),
  'World Challenge Cup / Group 3x+2x': groupRows(['UKR', 'ESP', 'BRA', 'MEX', 'EGY']),
}));

// ---- FIH static JSON ---------------------------------------------------------------------------------------
const fihTeams = [['GER', 'Germany', 3720.41], ['NED', 'Netherlands', 3600.2], ['BEL', 'Belgium', 3500.7], ['AUS', 'Australia', 3400.0], ['IND', 'India', 3300.5], ['ENG', 'England', 3200.9],
  ['ARG', 'Argentina', 3100.1], ['ESP', 'Spain', 3000.3], ['RSA', 'South Africa', 2000.0], ['NZL', 'New Zealand', 1900.6], ['FRA', 'France', 1800.2], ['MAR', 'Morocco', 400.0]];
write('Fih_Outdoor_Men.json', json({ ranks: fihTeams.map(([code, label, pts], i) => ({ rank: i + 1, team_short_code: code, team_name: label, points: pts })) }));

// ---- ICC: the ranking is data["bat-rank"].rank; shortname is a team code ------------------------------------
const icc = (date, codes) => json({ data: { 'bat-rank': { rank_date: date, rank: codes.map((c, i) => ({ no: i + 1, shortname: c, rating: 126 - i * 5, points: 3000 - i * 100 })) } } });
write('Icc_Test_Men.json', icc('2026-09-12', ['AUS', 'SA', 'ENG', 'IND', 'NZ', 'SL', 'WI', 'PAK', 'BAN', 'ZIM']));
write('Icc_T20_Women.json', icc('2026-09-12', ['AUS-W', 'ENG-W', 'IND-W', 'NZ-W', 'SA-W', 'WI-W', 'SL-W', 'PAK-W', 'HK-W', 'SRL-W', 'ESW-W', 'SDA-W']));
write('Icc_T20_Men.json', icc('2026-09-12', ['IND', 'AUS', 'ENG', 'STH', 'GSY', 'IOM', 'JSY', 'SRL', 'ESW', 'SDA', 'NEP', 'UAE']));

// ---- SVNS: the standings page, the two series, their standings ------------------------------------------------
const MEN_SERIES = '11111111-2222-4333-8444-555555555555';
const WOMEN_SERIES = '66666666-7777-4888-9999-000000000000';
write('Svns_Standings.html', `<!DOCTYPE html>
<html lang="en">
<head><meta charset="utf-8"><title>Sample standings page</title></head>
<body>
  <section class="series-standings" data-widget="svns/series-standings" data-series-ids="${MEN_SERIES},${WOMEN_SERIES}" data-flag-type="squares"></section>
</body>
</html>`);
write('Svns_Series_Men.json', json({ id: MEN_SERIES, name: 'Sample men\'s series', sport: 'mrs' }));
write('Svns_Series_Women.json', json({ id: WOMEN_SERIES, name: 'Sample women\'s series', sport: 'wrs' }));
const svns = (codes) => json({ entries: codes.map((c, i) => ({ position: i + 1, team: { abbreviation: c, countryCode: null }, totalPoints: 52 - i * 4 })) });
write('Svns_Standings_Men.json', svns(['RSA', 'ARG', 'FIJ', 'NZL', 'FRA', 'ESP', 'AUS', 'GBR', 'IRL', 'USA', 'KEN', 'URU']));
write('Svns_Standings_Women.json', svns(['AUS', 'NZL', 'FRA', 'USA', 'CAN', 'GBR', 'IRL', 'JPN', 'BRA', 'FIJ', 'ESP', 'RSA']));

// ---- Volleyball World: indoor teams with decimalPoints; beach pairs with player names and integer points ------
const indoor = [['POL', 'Poland', 400.16], ['ITA', 'Italy', 390.4], ['FRA', 'France', 380.2], ['JPN', 'Japan', 370.0], ['BRA', 'Brazil', 360.7], ['USA', 'USA', 350.1],
  ['SLO', 'Slovenia', 340.3], ['GER', 'Germany', 330.9], ['ARG', 'Argentina', 320.5], ['CUB', 'Cuba', 310.0], ['SRB', 'Serbia', 300.2], ['NZL', 'New Zealand', 40.0]];
write('VolleyballWorld_Men.json', json({ teams: indoor.map(([code, label, pts], i) => ({ rankToDisplay: i + 1, federationCode: code, name: label, decimalPoints: pts, points: Math.round(pts) })) }));
const beach = ['SWE', 'BRA', 'NOR', 'USA', 'BRA', 'GER', 'NED', 'QAT', 'ITA', 'POL', 'CZE', 'AUS'];
write('VolleyballWorld_Beach_Men.json', json({ teams: beach.map((code, i) => ({
  rankToDisplay: i + 1, federationCode: code,
  name: `${capitalized(2 * i)}/${capitalized(2 * i + 1)}, ${given(2 * i + 1)[0]}`,
  player1Name: titleCase(2 * i), player2Name: titleCase(2 * i + 1), points: 8020 - i * 400,
})) }));

// ---- WBSC: the rankings API rows and the page with the release dates -----------------------------------------
const wbscTeams = [['JPN', 1, 6337], ['USA', 2, 6000], ['KOR', 3, 5500], ['TPE', 4, 5300], ['VEN', 5, 4800], ['MEX', 6, 4500], ['DOM', 7, 4200], ['CUB', 8, 4000],
  ['NED', 9, 3800], ['GER', 10, 2000], ['PAN', 11, 1500], ['TUR', 11, 1500]];   // the bottom two tie
write('Wbsc_Baseball_Men.json', json({ rankings: wbscTeams.map(([ioc, position, points]) => ({ position, ioc, date: '2026-03-26', points })) }));
const releaseDates = [['2026-03-26', 'baseball-m'], ['2025-12-11', 'baseball-m'], ['2026-09-09', 'baseball-w'], ['2026-05-14', 'softball-m'], ['2025-11-20', 'softball-m'],
  ['2025-12-31', 'softball-w'], ['2026-08-07', 'baseball5-coed'], ['2026-02-01', 'baseball5-coed']]
  .map(([date, sport]) => ({ date, sport, year: Number(date.slice(0, 4)), formatted: `${date.slice(8)}/${date.slice(5, 7)}/${date.slice(0, 4)}` }));
write('Wbsc_Rankings.html', `<!DOCTYPE html>
<html lang="en">
<head><meta charset="utf-8"><title>Sample rankings page</title></head>
<body>
  <div id="app"></div>
  <script>window.releaseDates = ${JSON.stringify(releaseDates)};</script>
</body>
</html>`);

// ---- Wikipedia IIHF article (Parsoid HTML): a section per gender, a sortable table each --------------------------
const iihfRow = (rank, prev, team, gender, total) => {
  const link = `<a rel="mw:WikiLink" href="./${team.replace(/ /g, '_')}_${gender}'s_national_ice_hockey_team">${team}</a>`;
  return `        <tr><td>${rank}</td><td>${prev}</td><td style="text-align:left">${link}</td><td>${total === null ? '—' : total - 100}</td><td>${total === null ? '—' : `<b>${total}</b>`}</td></tr>`;
};
const iihfTable = (id, title, gender, ranked, unranked, marker) => `    <section>
      <h2 id="${id}">${title}</h2>
      <table class="wikitable sortable" style="text-align:center">
        <tbody>
        <tr><th>Rank</th><th>Previous</th><th>Team</th><th>Previous total</th><th>Current total</th></tr>
${ranked.map(([team, total], i) => iihfRow(i + 1, i + 1, team, gender, total)).join('\n')}
${unranked.map((team) => iihfRow(marker, marker, team, gender, null)).join('\n')}
        </tbody>
      </table>
    </section>`;
write('Wikipedia_IihfWorldRanking.html', `<!DOCTYPE html>
<html lang="en">
<head><meta charset="utf-8"><title>Sample IIHF World Ranking article</title></head>
<body>
  <section><h2 id="Overview">Overview</h2><p>A sample of the article's two ranking tables.</p></section>
${iihfTable("Men's_rankings", "Men's rankings", 'men',
    [['Switzerland', 5335], ['Canada', 5305], ['United States', 5280], ['Sweden', 5250], ['Finland', 5200], ['Czech Republic', 5150], ['Germany', 4900], ['Slovakia', 4700],
     ['Latvia', 4500], ['Denmark', 4300], ['Austria', 4100], ['Armenia', 805]],
    ['Russia', 'India', 'Morocco'], 'NR')}
${iihfTable("Women's_rankings", "Women's rankings", 'women',
    [['United States', 5460], ['Canada', 5400], ['Czech Republic', 5125], ['Finland', 5100], ['Sweden', 5000], ['Switzerland', 4900], ['Japan', 4800], ['Germany', 4600],
     ['Hungary', 4300], ['Singapore', 1135]],
    ['Philippines'], 'new')}
</body>
</html>`);

// ---- World Rugby union rankings ------------------------------------------------------------------------------
const rugby = ['RSA', 'IRL', 'NZL', 'FRA', 'ENG', 'ARG', 'SCO', 'AUS', 'FIJ', 'ITA', 'GEO', 'ASA'];
write('WorldRugby_Union_Men.json', json({
  label: 'Sample men\'s rankings', effective: { millis: 1789344000000, label: '2026-09-14' },
  entries: rugby.map((c, i) => ({ pos: i + 1, pts: i === 0 ? 95.09 : 90 - i * 3.5, team: { countryCode: c, name: c } })),
}));

// ---- WTA players API: a top-level array, one page --------------------------------------------------------------
const wta = ['CZE', 'USA', 'ITA', 'CAN', 'AUS', 'BEL', 'LAT', 'JPN', 'CHN', 'UKR', 'ESP', 'ROU'];
write('Wta_Doubles.json', json(wta.map((c, i) => ({
  player: { id: 300 + i, countryCode: c, fullName: titleCase(i) }, ranking: i + 1, points: 11460 - i * 500, rankedAt: '2026-09-14T00:00:00Z',
}))));
