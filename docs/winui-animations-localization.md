# WinUI 3 – analýza animací a lokalizace

## Současný stav

### Animace
- Aplikace centralizuje časy a easing křivky v resource dictionary `Resources/Animations.xaml`, kterou načítá `App.xaml`, takže styly `PulseButton` a `AnimatedExpander` jsou dostupné globálně.【F:Veriado.WinUI/App.xaml†L1-L18】【F:Veriado.WinUI/Resources/Animations.xaml†L1-L30】
- Třída `AnimationSettings` sleduje systémovou volbu *Reduce motion* a vyvolává událost při změně, na níž navazují animační helpery jako pulsování tlačítek či expanderů.【F:Veriado.WinUI/Helpers/AnimationHelpers.cs†L12-L168】
- `FilesPage` používá kompoziční API k řízenému cross-fade mezi stavem „načítám“ a výsledky, implicitním animacím položek repeateru a obsluze otevírání/zavírání `InfoBar` komponent.【F:Veriado.WinUI/Views/Files/FilesPage.xaml.cs†L61-L233】
- `ImportPage` kombinuje XAML přechody (`EntranceThemeTransition`) s nízkoúrovňovými kompozičními animacemi pro cross-fade panelů, zvýraznění „drop zóny“ a pulsování akčních prvků; všechny animace respektují `AnimationSettings`.【F:Veriado.WinUI/Views/Import/ImportPage.xaml†L37-L200】【F:Veriado.WinUI/Views/Import/ImportPage.xaml.cs†L200-L416】

### Lokalizace
- Texty uživatelského rozhraní jsou uloženy v `Strings/Resources.resw` a načítají se přes `x:Uid` nebo pomocnou metodu `LocalizedStrings.Get` pro scénáře mimo XAML (např. start aplikace).【F:Veriado.WinUI/Strings/Resources.resw†L1-L66】【F:Veriado.WinUI/Localization/LocalizedStrings.cs†L1-L37】【F:Veriado.WinUI/Views/StartupWindow.xaml.cs†L8-L22】
- Aplikace spoléhá na výchozí systémovou kulturu – runtime přepínání jazyka ani ukládání preferencí není k dispozici. Nastavení pracuje pouze s motivem, velikostí stránky a poslední složkou.【F:Veriado.WinUI/ViewModels/Settings/SettingsPageViewModel.cs†L1-L78】【F:Veriado.WinUI/Views/Settings/SettingsPage.xaml†L9-L32】

## Identifikované mezery

### Animace
- Chybí konzistentní sada motion patternů pro navigaci mezi stránkami (např. přechody při přepínání Frame) – současné animace se soustředí na lokální stavy.
- Animační helpery nejsou dokumentované a vývojáři mohou duplikovat logiku místo využití existujících přístupů (`Pulse`, `CrossFade`, implicitní animace).
- V některých částech UI (např. nové panely nebo data gridy) se animace nepoužívají vůbec, i když by zlepšily vnímání odezvy.

### Lokalizace
- `ImportPage` obsahuje mnoho pevně zakódovaných českých textů, což brání překladům do ostatních jazyků.【F:Veriado.WinUI/Views/Import/ImportPage.xaml†L64-L162】
- V projektech chybí guideline pro přidávání nových klíčů do `resw` a jejich validaci; rizikem jsou chybějící překlady nebo odlišné formátovací řetězce.
- Aplikace neumožňuje přepnout kulturu bez restartu a nemá infrastrukturu pro přetrvání volby uživatele.

## Návrhy na rozšíření

### Animace
1. **Navigační motion framework** – zaveďte jednotné používání `NavigationThemeTransition` a/nebo `ConnectedAnimationService` při přechodech mezi hlavními stránkami. Doplnit helper, který podle `AnimationSettings.AreEnabled` připojí vhodnou sadu přechodů, čímž se sjednotí chování a omezí duplikace kódu.
2. **Knihovna motion vzorů** – zdokumentujte a exportujte helper metody `CrossFade`, `Pulse` a implicitní kolekce jako opakovaně použitelné utility (např. do `AnimationHelpers`). Vývojáři tak budou mít jasnou cestu, jak animovat nové komponenty bez porušení preferencí uživatelů.【F:Veriado.WinUI/Helpers/AnimationHelpers.cs†L102-L168】【F:Veriado.WinUI/Views/Import/ImportPage.xaml.cs†L200-L416】
3. **Stavové animace pro seznamy a karty** – rozšiřte `ImplicitAnimationCollection` z `FilesPage` na další `ItemsRepeater`/`ListView` instance a přidejte jemné „reorder“ animace (offset + fade), aby změny dat působily plynule.【F:Veriado.WinUI/Views/Files/FilesPage.xaml.cs†L110-L163】
4. **Přístupnost a fallback** – v dokumentaci doplňte požadavek testovat UI s vypnutými animacemi (`AnimationSettings.AreEnabled == false`) a nabídněte alternativní vizuální indikátory (např. `ProgressRing.Visibility`), což je již částečně implementováno a je vhodné držet se stejného patternu.【F:Veriado.WinUI/Views/Files/FilesPage.xaml.cs†L61-L108】

### Lokalizace
1. **Deklarativní lokalizace importu** – převést `ImportPage` na `x:Uid` + `resw` klíče (včetně binding textů a `Content` tlačítek) a přidat nové řetězce do základního `Resources.resw`. Následně generovat prázdné položky pro všechny jazykové soubory, aby překladatelé snadno identifikovali nové texty.【F:Veriado.WinUI/Views/Import/ImportPage.xaml†L64-L162】【F:Veriado.WinUI/Strings/Resources.resw†L15-L66】
2. **Standardizace workflow** – vytvořit guideline: (a) pojmenovat `x:Uid` podle `{Page}_{Control}`; (b) používat `LocalizedStrings.Get` pouze ve ViewModel/servis vrstvě; (c) přidat kontrolu v CI, která porovná klíče mezi jednotlivými `.resw` soubory.
3. **Runtime přepínání jazyka** – doplnit službu, která dovolí uložit preferovanou kulturu a při startu ji znovu aplikovat (včetně reinitializace otevřených oken).【F:Veriado.WinUI/Localization/LocalizedStrings.cs†L1-L37】【F:Veriado.WinUI/App.xaml.cs†L67-L130】
4. **Formátovací testy** – pro řetězce s parametry (např. `Settings.PageSizeUpdated`) přidejte unit testy, které projdou dostupnými kulturami a ověří, že `string.Format` nevyhazuje výjimky. To minimalizuje riziko run-time chyb v lokalizovaném prostředí.【F:Veriado.WinUI/Strings/Resources.resw†L21-L29】

## Doporučený postup implementace
1. **Refaktor importního UI** – zavést `x:Uid` a přesunout texty do `resw`. Následně napojit existující helper animace (`PulseButton`, `CrossFade`) přes styly, aby se nově přidané prvky chovaly konzistentně.【F:Veriado.WinUI/Resources/Animations.xaml†L16-L29】【F:Veriado.WinUI/Views/Import/ImportPage.xaml.cs†L200-L416】
2. **Zpřístupnit animační helpery** – přesunout opakovaně používané metody z `ImportPage` do sdíleného helperu a rozšířit dokumentaci v repozitáři (README / wiki). Součástí může být vzor XAML, který nastaví `Grid.Transitions` a `ImplicitAnimations` s ohledem na `AnimationSettings`.
3. **Automatizovat lokalizační kontrolu** – doplnit skript nebo `dotnet` nástroj, který při CI porovná hlavní `Resources.resw` s jazykovými variantami a nahlásí chybějící klíče, čímž se včas zachytí nový obsah, jako je již existující stránka nastavení.【F:Veriado.WinUI/ViewModels/Settings/SettingsPageViewModel.cs†L63-L97】
4. **Pilotní scénář přepnutí kultury** – vytvořit testovací sekvenci: otevřít nastavení, změnit jazyk, sledovat, zda se `StartupWindow` nebo jiná otevřená okna zaktualizují; podle výsledků doplnit globální posluchače `CultureChanged` (např. v `MainShell`).【F:Veriado.WinUI/Views/StartupWindow.xaml.cs†L8-L22】【F:Veriado.WinUI/App.xaml.cs†L67-L118】

Tato doporučení zajistí konzistentní vizuální jazyk aplikace, respektují přístupnost a usnadní rozšiřování UI do dalších jazyků.
