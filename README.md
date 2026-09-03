# NetTrader-Lean

[![build](https://github.com/TigranGyan/nettrader-lean/actions/workflows/build.yml/badge.svg)](https://github.com/TigranGyan/nettrader-lean/actions/workflows/build.yml)

Мультиактивный (крипто + акции) систематический торговый движок на [QuantConnect LEAN](https://github.com/QuantConnect/Lean).

**Реальный статус сборки — только этот значок или вкладка [Actions](https://github.com/TigranGyan/nettrader-lean/actions).**
Заявление о прошедшей сборке в тексте коммита (в чате, PR-описании и т.п.) — не доказательство; несколько
раз в истории этого репозитория такое заявление оказывалось пустым (см. `docs/PLAN.md`).

Наследник проекта [`nettrader`](https://github.com/TigranGyan/nettrader) (самописный крипто-бот на Binance Futures) —
запускается с нуля на открытом движке вместо самописного исполнения ордеров/риск-логики, после того как диагностика
старого бота показала структурные причины устойчиво отрицательного результата (см. `docs/PLAN.md`, раздел «Контекст»).

## Phase 0 смоук-тест (PR #3, коммит `18bad2f`/`917f6aa`) — подтверждено

Первый реальный прогон `lean backtest` (BTCUSDT, 1m, 2024-01-01 → 2024-04-01) — **подтверждено, не выдумка**:
внутренне согласованные независимо сгенерированные файлы (`summary.json`, полный `results.json`,
`order-events.json`, 34MB `alpha-results.json`) дают одинаковое число ордеров (4676) и посчитанные в теге
Insight'а `RewardRiskRatio`/SL/TP реально совпадают с логикой `MlSignalTag.cs`. `MlSignalAlphaModel`
реально генерирует Insight'ы (46 620) и они реально доходят до ордеров, ран завершился `Status: Completed`
без ошибок. CI (`build.yml`) зелёный на этом коммите — https://github.com/TigranGyan/nettrader-lean/actions/runs/33757277204.

**Не верить цифрам доходности из этого прогона**: `Sharpe 16.74`, `+541% годовых` за 3 месяца на одном
символе — статистически абсурдные значения (вероятно неявное 100%-плечо через
`EqualWeightingPortfolioConstructionModel` + крошечная выборка), не индикатор реальной эджи. Это лишь
доказательство "сигнал генерируется и исполняется", не "стратегия работает" — для оценки доходности нужен
полный гейт Фазы 0 (7 символов, ≥1 год, `FractionalKellyPortfolioConstructionModel` вместо EqualWeighting).

**Замечание по гигиене репозитория**: результаты этого прогона (`Algorithm/backtests/2026-09-03_11-29-00/`)
весят **44MB** в рабочем дереве (`alpha-results.json` — 1.07M строк). Оставлено как есть — это первое
независимое доказательство, что сигнал реально работает, ценно как прецедент. **Но так каждый бэктест
коммитить нельзя** — при переходе на полный 7-символьный/годовой прогон (не говоря про paper-trading)
объём вырастет на порядки и раздует git-историю навсегда (blob'ы не сжимаются эффективно и не удаляются
без переписывания истории). Начиная со следующего прогона: коммитить только `summary.json`/`log.txt`
(лёгкие, достаточные как доказательство), сырые `*.json` с построчными Insight'ами — не коммитить (либо
хранить вне git, либо оставлять в `.gitignore` после того как разово проверены).

## Обновления от Jules (PR #1, коммит `5130739`)

Проверено построчным диффом против предыдущего состояния — изменения корректны:
- `NetTrader.Lean.Algorithm.csproj`: `QuantConnect.Lean.Engine` (неверный пакет — это движок-раннер, а не
  API для алгоритма) заменён на реальные `QuantConnect.Algorithm`/`Algorithm.Framework`/`Common`/
  `Indicators` 2.5.*, TFM поднят до `net10.0` — это актуальное требование этих пакетов, не производный
  выбор (проверено по `.nuspec` пакета на NuGet).
- Risk-модели (`CorrelationGroupExposureRiskManagementModel`, `DailyDrawdownRiskManagementModel`,
  `MarginRatioRiskManagementModel`) теперь наследуют `RiskManagementModel` (реальный базовый класс LEAN)
  вместо интерфейса `IRiskManagementModel` — сверено с исходником `QuantConnect/Lean`, сигнатура
  `ManageRisk` совпадает.
- **Найден и исправлен реальный баг**: в `MultiAssetAlgorithm.cs` `InsightWeightingPortfolioConstructionModel`
  (Фаза 0 по умолчанию, `UseKellySizing=false`) фильтрует insight'ы по `insight.Weight.HasValue` — а
  `MlSignalAlphaModel` никогда не передавал `weight:` в `Insight.Price(...)`. Это значило, что при первом
  бэктесте **не появилось бы ни одной сделки** — тихо, без ошибки. Jules заменил на
  `EqualWeightingPortfolioConstructionModel` (не требует `Weight`) — баг снят.
- Добавлен `Tests/` проект (юнит-тесты) — не разобран построчно в этом ревью, но не противоречит
  ничему из уже описанного.
- Из недостатков: три risk-модели получили неиспользуемый `using QuantConnect.Data.UniverseSelection;`
  (убран в этом коммите), `MultiAssetAlgorithm.cs` потерял комментарий про связь
  `Resolution.Minute` ↔ консолидаторы в `SymbolCandleCache.cs` (восстановлен). Компиляция по-прежнему
  не проверена ни разу вживую (заявление в коммите не подкреплено CI/логами/lock-файлом) —
  `dotnet restore && dotnet build` остаётся первым шагом.

## Статус: Фаза 0, single-user scope (см. `docs/PLAN.md`)

Область подтверждена: этот репозиторий — личная торговля одним пользователем, без Api/Auth/Stripe/
оркестратора (см. `docs/PLAN.md`, «Фаза 2b» — мульти-юзер отложен, не строится заранее).

**Что реально готово в этом коммите:**
- Структура репозитория и роудмап (`docs/PLAN.md`).
- Риск-слой с реализованной математикой: `FractionalKellyPortfolioConstructionModel`,
  `CorrelationGroupExposureRiskManagementModel`, `MarginRatioRiskManagementModel`,
  `DailyDrawdownRiskManagementModel`.
- **ML-сигнал перенесён, не переписан**: `Algorithm/Alpha/MlFeatureBuilder.cs` — построчный порт
  `MLSignalService.ComputeFeatures` на той же версии `Skender.Stock.Indicators` (2.7.1, запинена в
  `.csproj`), чтобы фичи не разъехались с тем, на чём обучена модель. `MlSignalAlphaModel.cs` грузит
  `ModelLong.zip`/`ModelShort.zip` (скопированы в `models/`, закоммичены — 2×~800KB, без Git LFS) через
  `PredictionEngine`, применяет гейт входа `p ≥ p_breakeven(R) + margin` вместо старого абсолютного
  порога 0.60 и эвристического динамического гэпа.
- SL/TP — **упрощённая** ATR-based версия (не полный volume-profile/HVN алгоритм из
  `GridMathCalculator.CalculateDynamicSlTpByVolumeProfile` — он не портирован, см. TODO в
  `MlSignalAlphaModel.ComputeStructuralSlTp`), но с базовым таргетом +3%/-2%, совпадающим с меткой
  обучения модели (в отличие от старого fallback 2%/1.5%, который ей не соответствовал).

**Что НЕ готово (честно, а не "и так сойдёт"):**
- Проект **не проверен компиляцией** — в песочнице, где писался код, нет `dotnet` SDK. Первое, что нужно
  сделать: `dotnet restore && dotnet build`.
- **Самый рискованный неподтверждённый кусок**: подписка на данные идёт как `Resolution.Minute`, а
  30m/1h/2h/4h свечи строятся консолидаторами (`TradeBarConsolidator`) в `MlSignalAlphaModel`/
  `SymbolCandleCache` — сама схема стандартна для LEAN, но не прогонялась ни разу. Проверить в первую
  очередь при первом бэктесте.
- BTC-контекст (`BtcTrend_1h`/`BtcRsi_1h`) частично работает (из кэша BTCUSDT), но `BtcDominance`
  (BTCDOMUSDT) и `FearGreedIndex` — не подключены, стоят на safe-fallback значениях (5000 / 50) как и в
  оригинале при недоступности данных — нужен реальный источник (см. TODO в `RefreshBtcContext`).
- Нет исторических данных (`data/`) — нужно скачать через LEAN ToolBox/Lean CLI.
- Нет брокерских ключей/конфигурации (Binance testnet, позже Alpaca) — без них возможен только бэктест.
- Ничего не бэктестилось и не paper-трейдилось — до реального капитала обязателен полный цикл гейтов
  из `docs/PLAN.md` (walk-forward валидация, ≥50-100 сделок в paper перед live).

## Быстрый старт (после того как SDK/Docker настроены)

```bash
# 1. Установить Lean CLI (управляет Docker-образом LEAN, данными, конфигом)
pip install lean

# 2. Скачать исторические данные по символам из Algorithm/Common/SymbolConfig.cs
lean data download --dataset "Binance Crypto Price Data" --market binance

# 3. Собрать и прогнать бэктест
dotnet build Algorithm/NetTrader.Lean.Algorithm.csproj
lean backtest Algorithm
```

## Структура

```
Algorithm/
  NetTrader.Lean.Algorithm.csproj
  MultiAssetAlgorithm.cs                     — точка входа QCAlgorithm, Фаза 0: только крипто
  Alpha/MlSignalAlphaModel.cs                — сигнал: гейт входа, загрузка .zip-моделей, SL/TP (упрощ.)
  Alpha/MlFeatureBuilder.cs                  — построчный порт MLSignalService.ComputeFeatures
  Alpha/MlSchema.cs                          — MlInput/BinaryMlOutput (точная копия схемы ML.NET)
  Alpha/SymbolCandleCache.cs                 — 30m/1h/2h/4h свечи per-symbol из консолидаторов LEAN
  PortfolioConstruction/FractionalKellyPortfolioConstructionModel.cs
  RiskManagement/CorrelationGroupExposureRiskManagementModel.cs
  RiskManagement/MarginRatioRiskManagementModel.cs
  RiskManagement/DailyDrawdownRiskManagementModel.cs
  Common/SymbolConfig.cs                     — 7 крипто-символов + группы корреляции (портировано из nettrader)
  Common/TradingOptions.cs                   — конфиг риска (портировано из nettrader)
  Common/MlSignalTag.cs                      — SL/TP/R в Insight.Tag (у LEAN Insight нет нативных полей)
docs/PLAN.md                                 — полный план миграции с фазами и гейтами
data/                                        — исторические данные LEAN (gitignored)
models/                                      — ModelLong.zip/ModelShort.zip — закоммичены (см. .gitignore)
```

## Почему LEAN, а не самописный движок

См. `docs/PLAN.md` — коротко: тот же язык (.NET/C#), открытый (Apache-2.0, можно коммерчески),
Algorithm Framework (Alpha/PortfolioConstruction/RiskManagement/Execution) даёт ровно то разделение
"сигнал / размер позиции / риск-правила", которого не хватало в старом коде, и встроенные комиссии/маржа
для Binance и Alpaca снимают целый класс багов старого бота (комиссии нигде не вычитались из PnL).

## Целевая доходность

10-25% годовых при управляемом риске (просадки 15-30% — норма для этого профиля). Никакая "формула" не
обещает устойчивые 10%/мес — это ~213% годовых, недостижимо легальными методами на дистанции;
подробное обоснование — в истории обсуждения, зафиксированной в `docs/PLAN.md`.
