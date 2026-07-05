# Alighten NinjaTrader 8 Indicators

This repository contains a proprietary suite of custom NinjaTrader 8 indicators developed for advanced order flow analysis, multi-timeframe pattern recognition, and chart visualization. The suite is centered around the **Alighten Mirror Dashboard** ecosystem and high-performance order flow engines.

## Repository Structure

The core scripts are located in the `NinjaTrader/Indicators/` directory.

### 📊 Alighten Mirror Dashboard Ecosystem
The Mirror ecosystem is a multi-timeframe pattern recognition and dashboard engine. It separates raw pattern detection (PtA-F) from the visualization and strategy-emission layer (Mirror).

* **`AlightenMirrorV0022.cs`** 
  * **Description:** The core Multi-Timeframe Dashboard and visualization engine. It consolidates signals from Patterns A, B, C, E, and F across multiple higher timeframes (HTFs) onto a single primary chart. It handles on-chart visual rendering (Direct2D lines/labels), Databox plot emissions for strategies, and HTF Volume Profile bias filtering.
* **`AlightenMirrorPtAV0008.cs`** (Delta Flip / Exhaustion)
  * **Description:** Core signal engine detecting price exhaustion and delta flips using a ZigZag/level tracking system.
* **`AlightenMirrorPtBV0003.cs`**
  * **Description:** Core signal engine for Pattern B detection.
* **`AlightenMirrorPtCV0005.cs`**
  * **Description:** Core signal engine for Pattern C detection.
* **`AlightenMirrorPtEV0006.cs`**
  * **Description:** Core signal engine for Pattern E detection.
* **`AlightenMirrorPtFV0001.cs`** (Breakout + Immediate Retest)
  * **Description:** Core signal engine tracking precise breakouts and immediate retests of critical structure levels.
* **`AlightenHTFVPV0004.cs`** (Higher Timeframe Volume Profile)
  * **Description:** Calculates the volume profile of an HTF bar and dynamically provides the trend bias data required by the Mirror dashboard for signal filtering and invalidation.

### 🔬 Order Flow & Volume Analytics
High-performance indicators for analyzing Bid/Ask delta, volume nodes, and institutional footprints.

* **`AlightenFootprintOrderFlowV00021.cs`**
  * **Description:** A comprehensive Footprint Indicator that aggregates Bid, Ask, Delta, Volume, Point of Control (POC), and Value Area metrics natively within NinjaTrader. Emits clean data arrays for integration with Bloodhound/strategies.
* **`AlightenHTFVPV0004.cs`**
  * **Description:** Higher Timeframe Volume Profile (HTFVP). Calculates the volume profile of an HTF bar and natively projects its POC and Value Area onto lower timeframe charts for the duration of the subsequent HTF bar. Used heavily for trend bias filtering.
* **`AlightenRelativeDeltaMultiTFV0002.cs`**
  * **Description:** Multi-Timeframe Relative Delta Wick Heatmap. Provides audio alerts when specific delta conditions align across multiple tracked timeframes.
* **`AlightenRelativeDeltaV0001.cs`**
  * **Description:** Base Relative Delta Footprint visualization.
* **`VolumeDelta.cs`**
  * **Description:** Core foundational Volume Delta calculation engine.

### 📈 Market Structure & Trend Analysis
* **`AlightenBiasV0003.cs`**
  * **Description:** Evaluates multi-level FTG (Failed To Go) and FTL (Failed To Lower) structures to dynamically determine the current market trend/bias.
* **`HigherTimeframeCandles.cs`**
  * **Description:** Projects higher-timeframe candlestick boundaries (Open, High, Low, Close) dynamically onto lower-timeframe charts.

### 🛠 UI / UX Utilities
Tools to enhance the NinjaTrader charting experience and streamline live execution.

* **`AlightenBarTimerV0004.cs`**
  * **Description:** An optimized Bar Timer that utilizes the Direct2D `OnRender` pipeline to completely eliminate the flashing/flickering common in standard UI-based bar timers.
* **`AlightenButtonPanelV0004.cs`**
  * **Description:** An interactive on-chart button panel providing quick access to Order Flow parameter controls and execution logic (e.g., "Breakeven + X Ticks").
* **`IndicatorVisualStyleHelper.cs`**
  * **Description:** Centralized helper class for managing uniform visual styling (brushes, strokes, fonts) across the Alighten indicator suite.
* **`LabelRemover.cs`**
  * **Description:** A clean-up utility that automatically removes cluttering text labels from all indicators loaded on a chart.
* **`OrderLineDecorator.cs`**
  * **Description:** Enhances the visual presentation of active order lines on the chart.

---
*Note: Legacy versions and WIP indicators designated with an `@` prefix have been excluded from this index.*
