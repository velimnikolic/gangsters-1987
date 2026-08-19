# Plan: ponašanje policije u igri

Stanje na dan 2026-08-19. Pokriva sim sloj (RoadDemo/CrewDemo), vezu sa strategijskim
slojem (ledger), i šta se NE radi. Građa perioda: `Docs/1987-period-reference.md`
(RICO, DEA, militarizacija policije, Chevrolet Caprice kao policijsko vozilo).

---

## 1. Šta već postoji (temelj, ne dirati bez razloga)

**Sim sloj — RoadDemo/CrewDemo (demo-lokalno, sve u `Assets/RoadDemo`):**
- `StreetAlarm` — jedini kanal za hice i smrti; incident (centar, tok, žrtve), upiti
  `Danger`/`QuietFor`. Sve novo se kači OVDE, ne na nove statike.
- `PoliceDispatch` — heat po incidentu (hici + tela, brojke iz WantedConfig), poziv
  1987-stilom: patrola čuje sama / svedok stigne do telefona / 45 s kap; lestvica
  kola po nivou (0/1/2/3), odredi Sent → Deploying → Responding → Warning
  („POLICE! DROP THE GUNS!") → Engaging → Securing (traka, čunjevi, „move along")
  → Leaving. `IPoliceUnit` apstrahuje ko dolazi.
- `PolicePatrolCar` — patrolna kola: Resting → Undocking → Patrolling (BFS waypoints
  po celom grafu) → Returning → Docking; + Responding/OnScene.
- `PoliceFootPatrol` — pozornik: isti ciklus peške, beat radijus 180 m od stanice.
- `PoliceCruiser` (CrewDemo) — CrewCar sa dva policajca kao jedinica zakona.
- Odobrene odluke: zalutali meci pogađaju civile; policija smrtonosna za SVAKOG ko
  puca posle upozorenja (i za našu ekipu); heat je demo-lokalan; bez hapšenja u demou.
  REVIDIRANO u fazi 0: naša ekipa se ne razoružava automatski na upozorenje — bori
  se i sa policijom kao i rival (v. dole).

**Parkirani sloj glavne igre (`Assets/Scripts/Gameplay`, isključen):**
- `WantedSystem`/`WantedConfig`/`WantedHud`, `PoliceResponseDirector/Officer/Car`
  (zvezdice 0–5, potera za IGRAČEM, hapšenje/ARRESTED karta), `NpcWitness`,
  `CrimeFeed`, `CorpseEvidence`. Ovo je model za igrača-mafioza; miruje dok se taj
  mod ne vrati.

**Strategijski sloj (ledger) — kuke već postoje:**
- `OrderType.Bribe` i `OrderType.EmployPolice` (Influence naredbe), `WeekSheet.Bribes`
  u računima, `Campaign` nedeljni sat. Efekat naredbi još nije vezan ni na šta.

---

## 2. Principi

1. **Policija je sila prirode, ne protivnik-šahista.** Reaguje na ono što vidi i čuje,
   procedurom iz 1987: telefon, dispečer, najbliža kola, sirena, uviđaj. Nema
   sveznanja — ako niko ne javi, niko ne dođe.
2. **Eskalacija je lestvica, ne prekidač.** Sitno → pozornik peške; pucnjava → kola;
   mrtav policajac → sve što stanica ima. Silazi polako (heat decay).
3. **Demo sloj ostaje demo-lokalan** ([[road-demo-self-contained]]): ništa iz
   RoadDemo ne piše u ledger direktno; most je tanak i jednosmeran (agregat nedelje).
4. **Posledice žive u strategijskom sloju**: novine, pritisak po kvartu, racije,
   mito — to je tabla, ne ulica.

---

## 3. Plan po fazama

### Faza 0 — ratni test u Play (prvo!) — KOD URAĐEN 2026-08-19, čeka Play
Ne samo verifikacija: policija IZLAZI iz kola i bori se sa obe zaraćene frakcije —
i našom i rivalskom — i ako ginu, ginu. Scenario: dva crew-a u ratu (CrewDemo, pa
RoadDemo sa rivalCrewsInCity), pucnjava, poziv, kola stižu, tri strane do kraja.
- **Revizija ranije odluke**: naša ekipa se više NE razoružava automatski na
  „POLICE! DROP THE GUNS!". U `DemoCrews.PoliceWarning` faction 0 gubi specijalni
  tretman — dobija isto što i rival u dometu: nastavlja rat, a na policiju okreće
  oružje (isti 60/40 valjak, ili prosto ostane u borbi pa ga policija pokupi).
- `PoliceDispatch.PickFight` već gađa „ko je pucao u zadnje 4 s" — u trosmernoj
  pucnjavi to hvata OBE frakcije; proveriti da dva odreda ne izaberu uvek istu
  metu (po odredu najbliža zaraćena ekipa, ne globalno ista).
- **Gubici stoje**: mrtav policajac ostaje mrtav, zbrisan odred = prazna kola kući
  (Wiped tok već postoji); pojačanje samo kroz heat lestvicu, dok stanica ima koga.
- Prolazni kriterijum: pucnjava tri strane se odigra bez zaglavljenih stanja, a
  posle tišine Securing (traka, move along) → Leaving normalno završi.

### Faza 1 — svakodnevica (policija kad se ništa ne dešava)
Cilj: da grad deluje policiran i pre prvog metka.
- **Smene i ritam**: patrole vezati za DemoClock — noću više kola, danju više peške;
  pauza na stanici između tura (postoji rest, dodati čitljive scene: dva pozornika
  na kafi ispred stanice, primopredaja smene).
- **Prisustvo kao signal igraču**: pozornik zastane, pogleda izlog, popriča sa
  civilom (interakcija u prolazu, kratko); patrolna kola uspore pored okupljene
  gomile. Čisto ambijent, bez nove logike odlučivanja.
- **Reakcija na sitnicu**: pozornik koji PROĐE pored leša (van incidenta, npr. telo
  ostalo posle pucnjave koju nije video) sam otvara mini-incident: stane, javi,
  dolazi jedno vozilo, scena zločina bez Engaging faze. Koristi postojeći
  Securing tok; ekvivalent parkiranog `CorpseEvidence`, ali demo-lokalno.

### Faza 2 — eskalacija odgovora (širenje postojeće lestvice)
- **Nivo 3–4 (danas: 2 kola)**: druga kola blokiraju ulicu — stanu POPREKO kolovoza
  na prilazu sceni (roadblock), `DriverNerve` već tera civilna kola da staju/beže.
- **Nivo 5 (danas: 3 kola)**: perimetar umesto juriša — kola staju na 35–40 m u tri
  pravca, ljudi iza vrata (zaklon `FindCover` već postoji), upozorenje preko
  megafona (baner + zvuk), pa tek onda Engaging. Policija koja je izgubila čoveka
  NE ulazi sama: čeka drugi odred (uslov u `TickSquad`: Engaging tek kad su ≥2
  odreda na sceni ili je protivnik ≤1).
- **Povlačenje**: odred sveden na jednog čoveka se povlači ka svojim kolima i drži
  distancu do pojačanja (danas se bore do kraja — nerealno i gubi dramu).
- **Kraj incidenta**: kad heat padne a pucači mrtvi/pobegli, jedna kola OSTAJU uz
  traku do isteka SceneSeconds; ostala odlaze ranije. (Danas svi čekaju isto.)

### Faza 3 — posle pucnjave: istraga (novo, i dalje demo-lokalno)
- **Uviđaj**: posle Securing stiže civilno vozilo (detektiv + fotograf iz
  kataloga tela — pazi na `IsGangBody`): kreda oko tela, fotografisanje (blic =
  postojeći muzzle-flash trik), odnošenje tela posle N minuta — tela ne leže večno.
- **Svedoci → opis**: civili koji su Gawk/Hide u trenutku pucnja pamte frakciju
  pucača; detektiv „ispituje" 1–2 svedoka (interakcija u mestu). Rezultat je
  demo-broj: `CaseHeat[faction]` — koliko je policija „na tragu" toj ekipi.
- **Prepoznavanje**: patrola koja naiđe na gangstera frakcije sa visokim CaseHeat
  uspori/zastane i „legitimiše" (kratka konfrontacija bez pucnjave); ako taj
  poteže — postojeći Engaging tok. Bez hapšenja (odluka stoji); ovo je pritisak,
  ne potera.

### Faza 4 — most ka ledgeru (strategijska policija)
Tanak, nedeljni agregat — sim šalje brojke, tabla odlučuje:
- **Police Pressure po kvartu (0–5)**: iz nedeljnih incidenata (hici, tela, mrtvi
  policajci — težine kao u dispatch heatu). Visok pritisak = više patrola u tom
  kvartu sledeće nedelje (spawn brojke), sporiji biznis (postojeći modifikatori
  prihoda), naslov u novinama (`HeadlineGenerator` već čita događaje).
- **Bribe / EmployPolice dobijaju efekat**: mito spušta Pressure kvarta i produžava
  vreme poziva (NobodyRang 45 s → 70 s u kupljenom kvartu); EmployPolice = imenovani
  kontakt u rosteru koji jednom nedeljno „skine" deo CaseHeat-a ili najavi raciju.
  Trošak ide u `WeekSheet.Bribes` (kolona već postoji).
- **Racija** (kasnije, glavna igra): Pressure 5 + CaseHeat prag → policija zatvara
  jedan biznis outfita na X nedelja (event na tabli, ne sim scena u prvoj verziji).
- **RICO luk (kasna igra)**: kumulativni federalni pritisak koji mito NE spušta —
  DEA/FBI sloj iznad gradske policije, tema perioda. Samo dizajn-beleška za sada.

---

## 4. Šta NE raditi
- Ne reciklirati parkirani WantedSystem/PoliceResponse u RoadDemo — demo ostaje
  samostalan; parkirani sloj čeka povratak igrača-mafioza.
- Bez hapšenja u sim sloju (odobrena odluka); hapšenja/zatvor su mehanika table.
- Policija ne puca prva ni na koga ko ne puca — upozorenje je uvek prvo.
- Bez helikoptera/SWAT-a u demou — 1987. gradska policija: kola, pozornici, sirena.
- Nikakav proceduralni dekor za stanice/scene van kataloga ([[blocks-catalog-only]]).

## 5. Redosled
1. Faza 0 (ratni test u Play: tri strane, gubici stoje) — bez ovoga je sve ostalo
   na pesku.
2. Faza 2 (eskalacija/povlačenje) — najveći dobitak drame za najmanje koda, sve u
   postojećem `TickSquad`.
3. Faza 1 (svakodnevica) — ambijent, može paralelno u malim komadima.
4. Faza 3 (istraga/CaseHeat) — novi podsistem, posle stabilne pucnjave.
5. Faza 4 (ledger most) — kad strategijska petlja nedelje bude aktivno igrana.

## 6. Otvorena pitanja
- Koliko jedinica po stanici na punoj mapi (perf: 3 kola + 2 pozornika po stanici?).
- Da li CaseHeat vidi igrač (UI) ili se samo oseća kroz ponašanje patrola.
- Detektivsko vozilo: koji model iz VehicleCatalog (neobeležena kola perioda).
