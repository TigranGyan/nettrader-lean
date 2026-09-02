# NetTrader-Lean

Мультиактивный (крипто + акции) систематический торговый движок на [QuantConnect LEAN](https://github.com/QuantConnect/Lean).

Наследник проекта [`nettrader`](https://github.com/TigranGyan/nettrader) (самописный крипто-бот на Binance Futures) —
запускается с нуля на открытом движке вместо самописного исполнения ордеров/риск-логики, после того как диагностика
старого бота показала структурные причины устойчиво отрицательного результата (см. `docs/PLAN.md`, раздел «Контекст»).

## Статус: Фаза 0 (см. `docs/PLAN.md`)

**Что реально готово в этом коммите:**
- Структура репозитория и роудмап (`docs/PLAN.md`).
- Скелет LEAN-алгоритма (`Algorithm/`) с реализованной математикой риск-слоя:
  `FractionalKellyPortfolioConstructionModel`, `CorrelationGroupExposureRiskManagementModel`,
  `MarginRatioRiskManagementModel`, `DailyDrawdownRiskManagementModel`.
- `MlSignalAlphaModel` — каркас с гейтом входа (`p ≥ p_breakeven(R) + margin`) и минимальным R:R,
  но **без** портированного 30-фичевого вектора и загрузки `.zip`-моделей — это то же самое, что уже
  посчитано в `nettrader/NetTrader.Infrastructure/MLServices/MLSignalService.cs`, и должно быть перенесено
  оттуда точно (не переписано заново), чтобы не внести случайное расхождение с обученной моделью.

**Что НЕ готово (честно, а не "и так сойдёт"):**
- Проект **не проверен компиляцией** — в песочнице, где писался код, нет `dotnet` SDK. Первое, что нужно
  сделать перед чем-либо ещё: `dotnet restore && dotnet build` и починить версии NuGet-пакетов LEAN, если они
  разъехались.
- Нет исторических данных (`data/`) — нужно скачать через LEAN ToolBox/Lean CLI для крипто-символов из
  `Algorithm/Common/SymbolConfig.cs`.
- `.zip`-модели ML (`ModelLong.zip`/`ModelShort.zip`) не скопированы из `nettrader` — положить в `models/`
  (папка в `.gitignore`, файлы не коммитить в git — большие бинарники, лучше Git LFS или внешнее хранилище).
- Нет брокерских ключей/конфигурации (Binance testnet, позже Alpaca) — без них возможен только бэктест.
- Ничего не бэктестилось и не паper-трейдилось — до реального капитала обязателен полный цикл гейтов
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
  Alpha/MlSignalAlphaModel.cs                — сигнал (TODO: портировать фичи/модели из nettrader)
  PortfolioConstruction/FractionalKellyPortfolioConstructionModel.cs
  RiskManagement/CorrelationGroupExposureRiskManagementModel.cs
  RiskManagement/MarginRatioRiskManagementModel.cs
  RiskManagement/DailyDrawdownRiskManagementModel.cs
  Common/SymbolConfig.cs                     — 7 крипто-символов + группы корреляции (портировано из nettrader)
  Common/TradingOptions.cs                   — конфиг риска (портировано из nettrader)
docs/PLAN.md                                 — полный план миграции с фазами и гейтами
data/                                        — исторические данные LEAN (gitignored)
models/                                      — .zip ML-модели (gitignored, положить руками)
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
