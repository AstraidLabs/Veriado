# WinUI 3 – analýza animací a lokalizace

## Současný stav

### Animace
- Aplikace centralizuje časy a easing křivky v resource dictionary `Resources/Animations.xaml`, kterou načítá `App.xaml`, takže styly `PulseButton` a `AnimatedExpander` jsou dostupné globálně.【F:Veriado.WinUI/App.xaml†L1-L18】【F:Veriado.WinUI/Resources/Animations.xaml†L1-L30】
- `AnimationSettings` sleduje systémovou volbu *Reduce motion*, nově ukládá `DispatcherQueue` UI vlákna a změny předává přes něj, takže odběratelé jako navigace či importní stránka aktualizují UI bezpečně.【F:Veriado.WinUI/Helpers/AnimationHelpers.cs†L14-L94】【F:Veriado.WinUI/App.xaml.cs†L34-L53】
- `FilesPage` používá kompoziční API k řízenému cross-fade mezi stavem „načítám“ a výsledky, implicitním animacím položek repeateru a obsluze otevírání/zavírání `InfoBar` komponent.【F:Veriado.WinUI/Views/Files/FilesPage.xaml.cs†L61-L233】
- `ImportPage` kombinuje XAML přechody (`EntranceThemeTransition`) s nízkoúrovňovými kompozičními animacemi pro cross-fade panelů, zvýraznění „drop zóny“ a pulsování akčních prvků; všechny animace respektují `AnimationSettings`.【F:Veriado.WinUI/Views/Import/ImportPage.xaml†L37-L200】【F:Veriado.WinUI/Views/Import/ImportPage.xaml.cs†L200-L416】

### Lokalizace
- Lokalizační servis je nyní zjednodušený na jedinou podporovanou kulturu `en-US`; načítá pouze výchozí `Resources.resw` a poskytuje formátované řetězce pro ViewModely.【F:Veriado.WinUI/Services/LocalizationService.cs†L1-L67】
- `LocalizedStrings` využívá stejný zdroj pro potřeby bootstrapu, takže texty při startu zůstávají konzistentní bez dynamického přepínání jazyků.【F:Veriado.WinUI/Localization/LocalizedStrings.cs†L1-L41】
- Nastavení UI obsahuje pouze volby vzhledu, stránkování a poslední složku; prvky pro výběr jazyka byly odstraněny společně s odpovídajícími řetězci.【F:Veriado.WinUI/Views/Settings/SettingsPage.xaml†L9-L32】【F:Veriado.WinUI/Strings/Resources.resw†L1-L40】
- Další jazykové `.resw` soubory byly odstraněny, takže build zahrnuje jen anglický zdroj a není třeba řešit konzistenci překladů při vývoji hlavní funkcionality.【F:Veriado.WinUI/Strings/Resources.resw†L1-L40】

## Identifikované mezery

### Animace
- Chybí konzistentní sada motion patternů pro navigaci mezi stránkami (např. přechody při přepínání Frame) – současné animace se soustředí na lokální stavy.
- Animační helpery nejsou dokumentované a vývojáři mohou duplikovat logiku místo využití existujících přístupů (`Pulse`, `CrossFade`, implicitní animace).
- V některých částech UI (např. nové panely nebo data gridy) se animace nepoužívají vůbec, i když by zlepšily vnímání odezvy.

### Lokalizace
- Lokalizace je aktuálně vypnutá (angličtina pouze); při opětovném zapnutí bude potřeba obnovit seznam podporovaných kultur, UI pro výběr jazyka a perzistenci volby.
- Doporučuje se vést seznam míst s pevnými texty (např. `ImportPage`) už teď, aby pozdější návrat lokalizace proběhl hladce.【F:Veriado.WinUI/Views/Import/ImportPage.xaml†L64-L162】
- Před opětovným zavedením přepínání jazyků bude nutné navrhnout mechanismus pro bezpečné přenačtení otevřených oken – současná `CultureAwarePage` zůstává připravená, ale zatím není aktivně používána.

## Návrhy na rozšíření

### Animace
1. **Navigační motion framework** – zaveďte jednotné používání `NavigationThemeTransition` a/nebo `ConnectedAnimationService` při přechodech mezi hlavními stránkami. Doplnit helper, který podle `AnimationSettings.AreEnabled` připojí vhodnou sadu přechodů, čímž se sjednotí chování a omezí duplikace kódu.
2. **Knihovna motion vzorů** – zdokumentujte a exportujte helper metody `CrossFade`, `Pulse` a implicitní kolekce jako opakovaně použitelné utility (např. do `AnimationHelpers`). Vývojáři tak budou mít jasnou cestu, jak animovat nové komponenty bez porušení preferencí uživatelů.【F:Veriado.WinUI/Helpers/AnimationHelpers.cs†L102-L168】【F:Veriado.WinUI/Views/Import/ImportPage.xaml.cs†L200-L416】
3. **Stavové animace pro seznamy a karty** – rozšiřte `ImplicitAnimationCollection` z `FilesPage` na další `ItemsRepeater`/`ListView` instance a přidejte jemné „reorder“ animace (offset + fade), aby změny dat působily plynule.【F:Veriado.WinUI/Views/Files/FilesPage.xaml.cs†L110-L163】
4. **Přístupnost a fallback** – v dokumentaci doplňte požadavek testovat UI s vypnutými animacemi (`AnimationSettings.AreEnabled == false`) a nabídněte alternativní vizuální indikátory (např. `ProgressRing.Visibility`), což je již částečně implementováno a je vhodné držet se stejného patternu.【F:Veriado.WinUI/Views/Files/FilesPage.xaml.cs†L61-L108】

### Lokalizace
1. **Deklarativní lokalizace importu** – převést `ImportPage` na `x:Uid` + `resw` klíče (včetně binding textů a `Content` tlačítek) a přidat nové řetězce do základního `Resources.resw`. Následně generovat prázdné položky pro všechny jazykové soubory, aby překladatelé snadno identifikovali nové texty.【F:Veriado.WinUI/Views/Import/ImportPage.xaml†L64-L162】【F:Veriado.WinUI/Strings/Resources.resw†L15-L66】
2. **Standardizace workflow** – vytvořit guideline: (a) pojmenovat `x:Uid` podle `{Page}_{Control}`; (b) používat `LocalizedStrings.Get` pouze ve ViewModel/servis vrstvě; (c) přidat kontrolu v CI, která porovná klíče mezi jednotlivými `.resw` soubory.
3. **Reakce na změnu kultury** – poskytovat helper (např. `CultureAwarePage`) naslouchající `ILocalizationService.CultureChanged`, který po změně kultury vyresetuje vlastní `ResourceContext` a případně znovu naváže `x:Bind`. Tím se zajistí, že již otevřená okna přeladí obsah bez nutnosti restartu i v prostředí WinUI.【F:Veriado.WinUI/Services/LocalizationService.cs†L32-L188】
4. **Formátovací testy** – pro řetězce s parametry (např. `Settings.PageSizeUpdated`) přidejte unit testy, které projdou všemi kulturami a ověří, že `string.Format` nevyhazuje výjimky. To minimalizuje riziko run-time chyb v lokalizovaném prostředí.【F:Veriado.WinUI/Strings/Resources.resw†L21-L29】

## Doporučený postup implementace
1. **Refaktor importního UI** – zavést `x:Uid` a přesunout texty do `resw`. Následně napojit existující helper animace (`PulseButton`, `CrossFade`) přes styly, aby se nově přidané prvky chovaly konzistentně.【F:Veriado.WinUI/Resources/Animations.xaml†L16-L29】【F:Veriado.WinUI/Views/Import/ImportPage.xaml.cs†L200-L416】
2. **Zpřístupnit animační helpery** – přesunout opakovaně používané metody z `ImportPage` do sdíleného helperu a rozšířit dokumentaci v repozitáři (README / wiki). Součástí může být vzor XAML, který nastaví `Grid.Transitions` a `ImplicitAnimations` s ohledem na `AnimationSettings`.
3. **Plán návratu lokalizace** – až bude aplikace funkčně kompletní, doplnit nové rozhraní pro výběr jazyka, obnovit perzistenci a navázat `CultureChanged`, přičemž lze znovu použít připravený `CultureAwarePage` základ.【F:Veriado.WinUI/Views/CultureAwarePage.cs†L1-L80】
4. **Pilotní scénář přepnutí kultury (po reaktivaci)** – před nasazením lokalizace připravit testovací sekvenci, která ověří přepnutí jazyků včetně zavřených i otevřených oken a návazných služeb.

Tato doporučení zajistí konzistentní vizuální jazyk aplikace, respektují přístupnost a usnadní rozšiřování UI do dalších jazyků.
