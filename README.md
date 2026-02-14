# Alighten Custom NinjaTrader 8 Tools

This repository contains a collection of custom Indicators and Market Analyzer Columns for NinjaTrader 8, developed under the "Alighten" namespace. These tools focus on Order Flow analysis, market structure patterns, and high-performance Market Analyzer integrations.

## Indicators

### Order Flow & Footprint
*   **AlightenFootprintOrderFlow** (`AlightenFootprintOrderFlowV00015.cs`)
    *   **Description:** A comprehensive Footprint chart implementation with extensive configuration for aggregation, signal detection, and visualization.
    *   **Key Features:**
        *   **Aggregation:** Custom interval, value area settings, session alignment.
        *   **Signals:** Detects Volume Sequencing, Stacked Imbalances, Divergences, Delta Flips, Sweeps, and more.
        *   **Visuals:** Highly customizable colors for bars, outlines, POC, and signal markers.
        *   **Live Updates:** Supports intrabar updates for real-time signal generation.

*   **AlightenOrderFlowTools** (`AlightenOrderFlowToolsV0005.cs`)
    *   **Description:** A clean-room implementation of order-flow trade detection tools.
    *   **Key Features:**
        *   **Large Trades:** Visualizes large trade bubbles based on volume/size filters.
        *   **Trapped Traders:** Identification of traders trapped by adverse price moves.
        *   **Stacked Imbalance Trap:** Signals for stacked imbalances followed by reversals.
        *   **Speed of Tape:** Real-time monitoring of tape speed (contracts/sec).

*   **AlightenSpeedOfTape** (`AlightenSpeedOfTapeV0001.cs`)
    *   **Description:** Visualizes the speed of the tape as a histogram with dynamic thresholds.
    *   **Key Features:**
        *   **Metrics:** Displays total Speed and Net Speed (Ask - Bid).
        *   **Threshold:** Dynamic threshold line based on statistical outliers (Mean + StdDev).
        *   **Display Modes:** Speed or Net Speed histogram styles.

*   **AlightenCVDRegressionBias** (`AlightenCVDRegressionBiasV0006.cs`)
    *   **Description:** Analyzes Cumulative Volume Delta (CVD) using regression to determine market bias.

### Market Structure Patterns (Multi-Timeframe)
A family of indicators designed to detect specific market structure patterns (A, B, C, D, E) based on higher-timeframe ZigZag pivots and levels, with optimized performance for Market Analyzer usage.

*   **AlightenLevelMultiTimeframeMATickPatternA** (`V0001`)
    *   **Description:** Market Analyzer core for "Pattern A". Uses HTF ZigZag/levels on primary series.
    *   **Features:** Live intrabar signal generation, proximity checks.

*   **AlightenLevelMultiTimeframeMATickPatternB** (`V0002`)
    *   **Description:** "Pattern B" detector.
    *   **Features:** Similar structural logic to Pattern A with specific gates (Pre-OG Check, TrendStart Check).

*   **AlightenLevelMultiTimeframeMATickPatternC** (`V0002`)
    *   **Description:** "Pattern C" detector.
    *   **Features:** Includes option to require a pivot for signal generation.

*   **AlightenLevelMultiTimeframeMATickPatternD** (`V0001`)
    *   **Description:** "Pattern D" detector.
    *   **Features:** Focuses on Zone touches/rejections defined by HTF pivots.

*   **AlightenLevelMultiTimeframeMATickPatternE** (`V0003`)
    *   **Description:** "Pattern E" detector (S/R Flip).
    *   **Features:** Identifies Support/Resistance flips with confirmation logic (break + retest). Supports live signal tags.

### Utilities
*   **AlightenAggregationAdvisor** (`AlightenAggregationAdvisor (1).cs`)
    *   **Description:** Helper tool for analyzing and advising on data aggregation settings.
*   **AlightenBarTimer** (`AlightenBarTimerV3.cs`)
    *   **Description:** A simple, efficient bar timer indicator.
*   **AlightenButtonPanel** (`AlightenButtonPanelV0004.cs`)
    *   **Description:** A dashboard panel for managing trading operations and toggling order flow features.

---

## Market Analyzer Columns

These columns are designed to provide a visual dashboard for the Pattern indicators, enabling efficient scanning across multiple instruments.

*   **AlightenMAPatternASignalColumn** (`V0002`)
*   **AlightenMAPatternBSignalColumn** (`V0002`)
*   **AlightenMAPatternCSignalColumn** (`V0001`)
*   **AlightenMAPatternDSignalColumn** (`V0002`)
*   **AlightenMAPatternESignalColumn** (`V0001`)

**Common Features:**
*   **Visual Signals:** Displays Up (🡅) or Down (🡇) arrows for active signals.
*   **Custom Coloring:** Configurable background colors for Long (Green), Short (Magenta), and Neutral states.
*   **Performance:** Uses `BarsToProcess` to limit historical calculation for faster loading.
*   **Integration:** Wraps the corresponding `AlightenLevelMultiTimeframeMATickPattern` indicator.

---

## Installation

1.  Place the `.cs` files in your NinjaTrader 8 custom directory:
    *   Indicators: `Documents\NinjaTrader 8\bin\Custom\Indicators`
    *   Market Analyzer Columns: `Documents\NinjaTrader 8\bin\Custom\MarketAnalyzerColumns`
2.  Compile the code in the NinjaTrader Editor (F5).
