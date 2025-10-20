🧭 1. London Breakout Algo (Classic & Reliable)
🧩 Concept

The market forms a range during the low-liquidity Asian session (6 p.m.–2 a.m. ET).
When London opens (~2 a.m. ET), you get a burst of volatility — usually breaking that range.

⚙️ Basic Rules

Define Asian range high/low between 6 p.m.–2 a.m. ET.

At 2 a.m. ET:

Place a buy stop a few ticks above the high.

Place a sell stop a few ticks below the low.

Use a stop on the opposite side of the range.

Profit target = 1–1.5× range size or trail via ATR.

✅ Works Best

On MNQ, MES, or even 6E/ES when the Asian range < ~40 points (NQ).

With a volatility filter (ATR > 5).

Avoid if U.S. data due before 8:30 a.m. — it can reverse violently.

⚡ Pro Tip

Add a false-break filter: wait for a 3-minute candle close beyond the range before triggering entry. Reduces whipsaws.

📈 2. VWAP Bounce / Mean Reversion Bot
🧩 Concept

Overnight price oscillates around VWAP — great for fading extremes when volume drops.

⚙️ Rules

Use anchored VWAP starting at 6 p.m. ET.

Go long when price closes > 1 ATR below VWAP and RSI < 30.

Go short when price closes > 1 ATR above VWAP and RSI > 70.

Exit on VWAP reversion or +0.75× ATR profit.

✅ Works Best

Low-news nights.

Session ranges < 50 points MNQ.

Pair with volume filter (no entry if volume < 50% of average).

🔀 3. Time-of-Day Trend-Bias Algo
🧩 Concept

From backtests, the first 90 minutes of the London session (2–3:30 a.m. ET) tend to trend in one direction before U.S. pre-market chop sets in.

⚙️ Rules

2 a.m. ET → Measure direction of first 15 min candle (up or down).

Trade with that direction using a pullback trigger:

EMA 9 > EMA 21 → long pullbacks to EMA 9.

EMA 9 < EMA 21 → short pullbacks to EMA 9.

Exit on EMA crossback or 1.5× ATR.

✅ Works Best

Volatile macro days.

When overnight range < half of average RTH range.

📊 4. Low-Volatility Scalper (Range Compression)
🧩 Concept

During slow nights (Asia + London overlap), small algos grind tiny ranges.

⚙️ Rules

Identify 10-bar Bollinger Band width < preset (e.g., 0.0025).

Place limit buy at lower band, limit sell at upper band.

Stop outside opposite band, profit at midpoint.

Disable after 4 a.m. ET when volatility expands.

✅ Works Best

Flat, low-volume Asian nights (Sunday/Monday).

Works on MNQ, MES, 6E, CL (with adjusted tick values).

🧠 5. Hybrid ATR-Triggered Session Scalper
🧩 Concept

Combine volatility detection + directional bias.

⚙️ Rules

30-min rolling ATR (e.g., ATR(14) × multiplier).

When ATR > threshold → enable trend-following logic (EMA cross or Heiken-Ashi trend).

When ATR < threshold → enable mean-reversion logic (VWAP fade).

Essentially one NinjaScript toggle switches strategies as volatility changes.

⚙️ 6. Risk & Execution Tips for Overnight Algos
Rule	Why
Use limit orders	Thin liquidity = avoid market slippage
Trade ½ size	Vol spikes randomly
Always flatten before 4:55 p.m. ET	CME closes for maintenance
Check exchange margins nightly	Varies before macro events
Track performance separately	Overnight stats differ from RTH
