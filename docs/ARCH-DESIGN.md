# Architektura domény a DTO

## Doména v projektu `Veriado.Domain`

Doménové entity, hodnotové objekty i doménové události patří výhradně do projektu `Veriado.Domain`. Doména tak zůstává čistá a nezávislá na infrastruktuře, což umožňuje jednotné chování napříč perzistencemi, zjednodušuje testování a chrání před nechtěným únikem EF Core atributů či konverzí mimo infrastrukturu.

## Systémové kontrakty v `Veriado.Contracts`

DTO používané na hranici systému (WinUI, API nebo další klienti) musí být definované v `Veriado.Contracts`. Jednotná sada kontraktů zaručuje konzistenci mezi klienty, minimalizuje závislost prezentační vrstvy na aplikační logice a dovoluje sdílet kontrakty mezi různými front-endy bez nutnosti sahat do aplikační vrstvy.

## Interní modely v aplikační vrstvě

Vrstva `Veriado.Application` může mít vlastní interní modely, příkazy a dotazy optimalizované pro use-casy či čtení z databáze. Tyto typy však zůstávají čistě interní a nesmí prosakovat do UI ani do služeb – mapování na DTO z `Veriado.Contracts` se provádí těsně před hranicí systému, typicky ve vrstvě mapování (`Veriado.Mapping`) nebo ve službách.
