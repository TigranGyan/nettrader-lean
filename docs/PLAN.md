# NetTrader: миграция торгового движка на LEAN (крипто + акции)

## Контекст

Диагностика текущего NetTrader (Binance-only crypto futures bot) выявила структурные причины устойчиво отрицательного результата:
- комиссии/funding нигде не вычитаются из PnL и win/loss статистики (`TradeRepository.GetPnlStatsAsync` — только знак `LastKnownPnL`, плюс отдельный баг: `EmergencyStopped` включён в знаменатель, но исключён из `winStatuses` → эмерджи-стоп с плюсовым PnL молча засчитывается как убыток);
- минимальный R:R нигде не проверяется кодом (только пожелание в промпте Gemini);
- размер позиции фиксирован (20% от balance×leverage), не зависит от уверенности сигнала — Kelly не считается;
- лимит "1 позиция на группу корреляции" — только в промпте, не в валидаторе;
- нет проверки margin ratio / расстояния до ликвидации;
- расхождение документации и кода по порогам ML (`CLAUDE.md` устарел: код `MLSignalService.Decide()` использует `minRequiredConfidence=0.60` + динамический гэп 0.02–0.10, а не `MinGap=0.02/Threshold=0.42` из доков).

Пользователь отклонил цель "10%/мес" как недостижимую легальными методами (≈213% годовых) и утвердил:
- **целевая доходность: 10–25% годовых при управляемом риске** (просадки 15–30% — норма для такого профиля, не красный флаг);
- **архитектурное решение: заменить самописный движок на открытый мультиактивный (QuantConnect LEAN)**, чтобы одновременно получить и крипту, и акции, а не чинить риск-слой в изоляции.

Ниже — план этой миграции.

## Почему LEAN, а не альтернатива

LEAN (Apache-2.0, самохостится в Docker) — предпочтительный выбор именно потому, что:
- **тот же язык/рантайм**: алгоритмы LEAN — обычные C# class library, `NetTrader.Domain` (Entities, `SymbolConfig` с группами корреляции, `TradingOptions`) подключается напрямую без моста; существующие ML.NET модели (`ModelLong.zip`/`ModelShort.zip`) грузятся в LEAN-алгоритм без переобучения — это просто NuGet-пакет `PredictionEngine`/`PredictionEnginePool`;
- лицензия допускает коммерческий SaaS (у проекта уже есть Stripe-биллинг);
- Algorithm Framework (Alpha / PortfolioConstruction / RiskManagement / Execution модели) почти один-в-один ложится на нужное разделение "сигнал / размер позиции / риск-правила / исполнение" — именно то разделение, которого сейчас нет (`GeminiAdvisor` сейчас одним вызовом решает и направление, и размер, и SL/TP).

Альтернатива Nautilus Trader (Rust/Python-ядро) отклонена: пришлось бы либо переобучать ML-модели на Python, либо городить gRPC/Redis-мост между .NET-инференсом и Python-стратегией — лишняя латентность и точка отказа без выигрыша для соло/малой .NET-команды.

**Честно про главный риск миграции**: NetTrader — мультитенантный BYOK-сервис (у каждого пользователя свои ключи биржи), а LEAN нативно рассчитан на модель "один алгоритм = одно брокерское подключение = один портфель". Даже QuantConnect Cloud решает мультитенантность через один контейнер на пользователя. Это меняет топологию деплоя: было "один API-контейнер на всех", станет "один API/оркестратор + один LEAN-контейнер на каждого активного live-пользователя". Это отдельный, некоммерческий кусок инфраструктурной работы, не мат-модель — закладываем его отдельным под-этапом в Фазе 2, не размываем.

## Целевая архитектура

**Остаётся как есть:** `NetTrader.Api` (Auth, JWT, `RefreshTokenStore`, CORS, Program.cs), контроллеры Positions/Balance/Trades/Payments, `TradingHub` (SignalR), Postgres-схема auth/users/payments, фронтенд-контракт (Vercel не меняется). `TradeSession`/`GridOrder`/`PnlStats` таблицы — репурпонятся как read-модель для фронтенда, не удаляются. `IApiKeyEncryptionService` — нужен по-прежнему, просто ключи идут в LEAN-конфиг вместо `IOrderExecutorFactory`.

**Удаляется:** `GridTradingManager.cs` (833 строки — заменяется циклом LEAN Algorithm Framework), `GridMathCalculator.cs` (ATR/volume-profile SL/TP логика переносится, но не как отдельный класс), `IndicatorEnrichmentService.cs` (самописные RSI/ADX/EMA/MACD/ATR/Bollinger → нативные индикаторы LEAN — убирает целый класс багов "совпадает ли наша реализация с тем, на чём обучалась модель"), `BinanceFuturesClient.cs`/`BybitClient.cs` (→ поддерживаемые брокерские плагины `QuantConnect.Brokerages.Binance`/Alpaca — заодно бесплатно получаем корректные комиссии), `GridSettingsValidator`/`GridSettingsListValidator` (R:R и лимиты корреляции переезжают в `RiskManagementModel`, где они реально проверяются на каждый target, а не только "если AI выполнит промпт"), Triple-Loop `TradingBotWorker` (→ `Schedule.On`/`Consolidate` в LEAN; воркер-проект скорее всего ужимается до Telegram-command-intake).

**Добавляется:** `NetTrader.Lean.Algorithm` (алгоритм LEAN, ссылается на `NetTrader.Domain`), `NetTrader.Lean.Orchestrator` (старт/стоп/мониторинг LEAN-деплоев по тенантам, трансляция состояния LEAN обратно в `TradeSession`/`PnlStats` — может первоначально жить как hosted service внутри `NetTrader.Api`), Docker-образы LEAN в `docker-compose.yml` рядом с `api`/`caddy`.

## ML и Gemini внутри LEAN

- **`MlSignalAlphaModel`** (новый `AlphaModel`): держит индикаторы LEAN на нужных таймфреймах, строит тот же 30-фичевый вектор, что и сейчас `MLSignalService`, гоняет существующие `.zip`-модели без переобучения (крипто, Фаза 0–2), считает ATR-based SL/TP (портируя `CalculateDynamicSlTpByVolumeProfile`, но **исправляя** несоответствие меткам обучения — сейчас fallback 2%/1.5% не совпадает с тренированным барьером +3%/-2%), из этого получает `R`, применяет гейт входа и эмитит `Insight` с `R`/SL/TP в `Insight.Tag` (у LEAN Insight нет нативных полей SL/TP — Tag это идиоматичный носитель).
- **Роль Gemini сжимается**: было — `AnalyzeAndSelectGridsAsync` возвращает готовый размер+SL+TP (то есть Gemini de facto был риск-слоем, что и было одной из причин проблемы). Станет — советующий вход по режиму рынка (`MacroRegimeRiskManagementModel` или множитель уверенности), контракт `{riskMultiplier: 0..1, perSymbolVeto: [...]}` на основе `MacroAnalyzer` (FGI/BTC.D/FOMC) + качественного вывода Gemini. **Gemini больше не задаёт размер позиции и SL/TP.**

## Риск-слой как компоненты LEAN (математика из диагностики)

- **`FractionalKellyPortfolioConstructionModel`** (кастомный, не встроенный `InsightWeightingPortfolioConstructionModel` — тот не знает про асимметричный Kelly и `R` на сигнал): `f = p − (1−p)/R`, клип на `[0, fCap]`, множитель `KellyFraction` (по умолчанию 0.5 = half-Kelly), масштаб по свободной марже и `MaxMarginUsagePercent`.
- **Гейт входа**: `p_model ≥ p_breakeven(R) + запас`, где `p_breakeven = 1/(1+R)` — считается в Alpha-модели вместе с `R`.
- **Минимальный R:R** — жёсткий пол (напр. `MinRewardRiskRatio = 1.5`), независим от `p`.
- **Лимиты по группам корреляции** — новый `CorrelationGroupExposureRiskManagementModel`, использует существующий `SymbolConfig` (переиспользуется как есть, без дублирования).
- **Комиссии/funding** — встроенные `BinanceFeeModel`/`BinanceFuturesFeeModel`/`AlpacaFeeModel` решают проблему "комиссия нигде не вычитается" на уровне движка бесплатно; funding для перпетуалов — проверить, есть ли нативно в текущей версии брокерской модели LEAN, если нет — добавить `Schedule.On` каждые 8ч.
- **Просадка/margin ratio** — встроенный `MaximumDrawdownPercentPortfolio` как база + кастомная проверка под `DailyDrawdownPausePercent`/`EmergencyStopBalancePercent`/`MaxOpenPositions` (эти опции из `TradingOptions.cs` переиспользуются как конфиг) + новый кастомный `RiskManagementModel` на `TotalMarginUsed`/`TotalPortfolioValue` для дистанции до ликвидации — этого не было вообще, пишется с нуля.

## Акции

Брокер — **Alpaca** (комиссия 0%, зрелая интеграция в LEAN, простой paper-trading, ключ+секрет — та же модель BYOK, что уже есть для Binance). IB — опция позже, если понадобятся инструменты вне Alpaca.
**Важно**: текущие ML-модели обучены на крипто-специфичной метке (+3%/-2% за 12ч, 24/7-рынок) — **не переиспользовать для акций**. Для старта Фазы 3 — простая rules-based `EquitiesAlphaModel` на нативных индикаторах LEAN (без ML), честно без псевдо-точности необученной модели; отдельная ML-модель на акции — отдельное решение позже, когда будет история для валидации.

## Данные

- **Крипто**: исторические klines Binance через LEAN ToolBox/community data-source — бесплатно, этого достаточно для 7 whitelisted символов.
- **Акции**: платная подписка QuantConnect — самый надёжный вариант (point-in-time сплиты/дивиденды), либо бесплатно через Alpaca historical bars/Stooq/Yahoo — но с оговоркой: бесплатные данные годятся, чтобы нащупать форму стратегии, не как финальный гейт перед реальным капиталом (нужна ре-валидация на точных данных перед увеличением размера позиции).

## Фазы и критерии перехода

**Фаза 0 — LEAN в Docker, только бэктест, без влияния на пользователей.**
Поднять LEAN-сервис в compose, зафиксировать версию/коммит. Создать `NetTrader.Lean.Algorithm`, портировать индикаторы (сверить пару исторических баров старый-код vs LEAN-нативный на предмет расхождений формул). Собрать `MlSignalAlphaModel` на встроенной `InsightWeightingPortfolioConstructionModel` (ещё не Kelly) — только чтобы проверить генерацию Insight.
*Гейт:* бэктест ≥1 год по 7 символам проходит чисто, распределение направления сигналов примерно совпадает с историческими логами текущего `Decide()`, индикаторы не разъезжаются.

**Фаза 1 — Paper-trading крипты через LEAN + ML, параллельно с текущим live-ботом.**
Добавить реальную `FractionalKellyPortfolioConstructionModel`, гейт входа, `CorrelationGroupExposureRiskManagementModel`, риск по просадке/марже. Paper-trading на Binance testnet, только house-аккаунт (без мультитенантности).
*Гейт:* ≥50–100 закрытых сделок, walk-forward Sharpe/Sortino выше заданного порога **на out-of-sample окне** (не на том, где подбирались Kelly-fraction/гейт), просадка в пределах 15–30%, комиссии/проскальзывание в paper совпадают с бэктест-прогнозом.

**Фаза 2 — Перевод крипты на live LEAN, демонтаж старого пути.**
Убрать `GridTradingManager`, `GridMathCalculator`, `BinanceFuturesClient`, старые валидаторы, `TradingBotWorker`. Сначала live на house-капитале. Отдельным под-этапом — `NetTrader.Lean.Orchestrator` (по одному LEAN-деплою на тенанта с его BYOK-ключами), с собственным paper-циклом валидации до перевода не-admin пользователей.
*Гейт:* live-результаты house-капитала совпадают с paper в пределах ожидаемого шума; оркестратор переживает как минимум одну учебную рестарт/креш-рекавери; `PnlStats`/фронтенд-контракт подтверждённо питаются от LEAN.

**Фаза 3 — Акции через Alpaca.**
`EquitiesAlphaModel` (rules-based, без переиспользования крипто-модели). Paper-trading отдельно, свои критерии Sharpe/просадки.
*Гейт:* акции проходят свой walk-forward гейт независимо от крипто-цифр.

**Фаза 4 — Перепривязка `SignalRBroadcastService` и контроллеров к состоянию LEAN** вместо прямых вызовов биржи.
*Гейт:* поведение для фронтенда не меняется (те же формы данных, та же задержка) — полный регресс-прогон контрактов.

## Проверка на каждом этапе

- **Walk-forward валидация везде, где трогаются параметры стратегии** (Kelly fraction, запас гейта, пол R:R, лимиты корреляции) — скользящие окна (например 6 мес train / 1 мес out-of-sample, прокатка по всей истории). Это прямое требование после уже однажды найденного и исправленного data leakage в этом же проекте — нельзя повторить на новом слое.
- **Paper-trading — обязательный гейт** перед любым увеличением live-капитала на любой фазе; ни одна фаза не продвигается по одному только бэктесту.
- Сейчас в `NetTrader.Tests` нет инфраструктуры для бэктестов/walk-forward (только юнит-тесты `GridMathCalculatorTests`/`GridSettingsValidatorTests`/`GridTradingManagerMonitoringTests`) — Фаза 0 должна включать минимальный автоматизированный harness (даже скрипт, запускающий LEAN CLI на зафиксированных данных с проверкой границ статистик), чтобы дальнейшие фазы имели повторяемую проверку при каждом изменении Alpha/Risk моделей.

### Критические файлы
- `NetTrader.Infrastructure/MLServices/MLSignalService.cs`
- `NetTrader.Infrastructure/AiServices/GeminiAdvisor.cs`
- `NetTrader.Application/Services/GridTradingManager.cs`
- `NetTrader.Application/Calculations/GridMathCalculator.cs`
- `NetTrader.Domain/Constants/SymbolConfig.cs`
- `NetTrader.Domain/Options/TradingOptions.cs`
- `NetTrader.Domain/Interfaces/IUserExchangeExecutorFactory.cs`
- `NetTrader.Infrastructure/Repositories/TradeRepository.cs`
- `docker-compose.yml`
