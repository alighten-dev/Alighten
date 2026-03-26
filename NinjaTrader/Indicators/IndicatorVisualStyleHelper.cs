#region Using declarations
using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
  internal static class IndicatorVisualStyleHelper
  {
    private static readonly FontFamily DefaultFontFamily = new FontFamily("Segoe UI");

    private static readonly Color PrimaryTop = Color.FromRgb(0, 120, 215);
    private static readonly Color PrimaryBottom = Color.FromRgb(0, 100, 195);
    private static readonly Color PrimaryBorder = Color.FromRgb(0, 70, 160);
    private static readonly Color PrimaryHoverTop = Color.FromRgb(0, 140, 235);
    private static readonly Color PrimaryHoverBottom = Color.FromRgb(0, 120, 215);
    private static readonly Color PrimaryHoverBorder = Color.FromRgb(0, 90, 180);

    private static readonly Color DangerTop = Color.FromRgb(200, 40, 40);
    private static readonly Color DangerBottom = Color.FromRgb(160, 30, 30);
    private static readonly Color DangerBorder = Color.FromRgb(140, 20, 20);
    private static readonly Color DangerHoverTop = Color.FromRgb(220, 70, 70);
    private static readonly Color DangerHoverBottom = Color.FromRgb(190, 40, 40);
    private static readonly Color DangerHoverBorder = Color.FromRgb(170, 30, 30);

    private static ControlTemplate cachedRoundedTemplate;

    public static Button CreateSettingsButton(string content, string automationId, RoutedEventHandler clickHandler)
    {
      var button = new Button
      {
        Content = content,
        Foreground = Brushes.White,
        Margin = new Thickness(6, 3, 6, 3),
        Padding = new Thickness(16, 8, 16, 8),
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        FontSize = 11,
        FontWeight = FontWeights.Bold,
        FontFamily = DefaultFontFamily,
        BorderThickness = new Thickness(1),
        Height = 28,
        Width = 90
      };

      ConfigureGradientButton(button,
        PrimaryTop, PrimaryBottom, PrimaryBorder,
        PrimaryHoverTop, PrimaryHoverBottom, PrimaryHoverBorder,
        addDropShadow: true);

      if (!string.IsNullOrEmpty(automationId))
        AutomationProperties.SetAutomationId(button, automationId);

      if (clickHandler != null)
        button.Click += clickHandler;

      return button;
    }

    public static void ApplyDialogChrome(Window window)
    {
      if (window == null)
        return;

      window.Background = new SolidColorBrush(Color.FromRgb(28, 28, 28));
      window.Foreground = Brushes.White;
      window.FontFamily = DefaultFontFamily;
    }

    public static TextBlock CreateSettingLabel(string text, Thickness? margin = null)
    {
      return new TextBlock
      {
        Text = text,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = Brushes.White,
        FontSize = 13,
        Margin = margin ?? new Thickness(0, 0, 12, 0),
        FontFamily = DefaultFontFamily
      };
    }

    public static TextBox CreateNumericTextBox(double width = 110)
    {
      return new TextBox
      {
        Width = width,
        Height = 24,
        Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
        Foreground = Brushes.White,
        BorderBrush = Brushes.Gray,
        BorderThickness = new Thickness(1),
        HorizontalContentAlignment = HorizontalAlignment.Center,
        FontSize = 13,
        FontFamily = DefaultFontFamily
      };
    }

    public static CheckBox CreateDialogCheckBox(string content)
    {
      return new CheckBox
      {
        Content = content,
        Foreground = Brushes.White,
        Margin = new Thickness(0, 8, 12, 0),
        FontSize = 13,
        FontFamily = DefaultFontFamily
      };
    }

    public static Button CreatePrimaryDialogButton(string content, RoutedEventHandler clickHandler)
    {
      var button = CreateDialogButtonCore(content);
      ConfigureGradientButton(button,
        PrimaryTop, PrimaryBottom, PrimaryBorder,
        PrimaryHoverTop, PrimaryHoverBottom, PrimaryHoverBorder,
        addDropShadow: true);

      if (clickHandler != null)
        button.Click += clickHandler;

      return button;
    }

    public static Button CreateDangerDialogButton(string content, RoutedEventHandler clickHandler)
    {
      var button = CreateDialogButtonCore(content);
      ConfigureGradientButton(button,
        DangerTop, DangerBottom, DangerBorder,
        DangerHoverTop, DangerHoverBottom, DangerHoverBorder,
        addDropShadow: true);

      if (clickHandler != null)
        button.Click += clickHandler;

      return button;
    }

    public static void ConfigureGradientButton(Button button,
      Color normalTop, Color normalBottom, Color borderColor,
      Color hoverTop, Color hoverBottom, Color hoverBorder,
      bool addDropShadow)
    {
      if (button == null)
        return;

      button.Background = CreateGradient(normalTop, normalBottom);
      button.BorderBrush = new SolidColorBrush(borderColor);
      button.BorderThickness = new Thickness(1);
      button.Template = GetRoundedButtonTemplate();

      if (addDropShadow)
      {
        button.Effect = new DropShadowEffect
        {
          Color = Colors.Black,
          Direction = 270,
          ShadowDepth = 3,
          Opacity = 0.4,
          BlurRadius = 4
        };
      }

      button.MouseEnter += (s, e) =>
      {
        button.Background = CreateGradient(hoverTop, hoverBottom);
        button.BorderBrush = new SolidColorBrush(hoverBorder);
      };

      button.MouseLeave += (s, e) =>
      {
        button.Background = CreateGradient(normalTop, normalBottom);
        button.BorderBrush = new SolidColorBrush(borderColor);
      };
    }

    private static Button CreateDialogButtonCore(string content)
    {
      return new Button
      {
        Content = content,
        Width = 100,
        Height = 32,
        Margin = new Thickness(8, 0, 0, 0),
        Foreground = Brushes.White,
        FontSize = 13,
        FontFamily = DefaultFontFamily,
        FontWeight = FontWeights.SemiBold,
        HorizontalAlignment = HorizontalAlignment.Right,
        Padding = new Thickness(12, 4, 12, 4)
      };
    }

    private static LinearGradientBrush CreateGradient(Color top, Color bottom)
    {
      return new LinearGradientBrush(top, bottom, 90);
    }

    private static ControlTemplate GetRoundedButtonTemplate()
    {
      if (cachedRoundedTemplate != null)
        return cachedRoundedTemplate;

      var template = new ControlTemplate(typeof(Button));

      var borderFactory = new FrameworkElementFactory(typeof(Border));
      borderFactory.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
      borderFactory.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
      borderFactory.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
      borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));

      var presenterFactory = new FrameworkElementFactory(typeof(ContentPresenter));
      presenterFactory.SetBinding(ContentPresenter.ContentProperty, new System.Windows.Data.Binding("Content") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
      presenterFactory.SetBinding(ContentPresenter.HorizontalAlignmentProperty, new System.Windows.Data.Binding("HorizontalContentAlignment") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
      presenterFactory.SetBinding(ContentPresenter.VerticalAlignmentProperty, new System.Windows.Data.Binding("VerticalContentAlignment") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });

      borderFactory.AppendChild(presenterFactory);
      template.VisualTree = borderFactory;

      cachedRoundedTemplate = template;
      return cachedRoundedTemplate;
    }
	
	// Add to IndicatorVisualStyleHelper
	public static RadioButton CreateDialogRadioButton(string content, string groupName = null)
	{
	  return new RadioButton
	  {
	    Content = content,
	    GroupName = groupName ?? "grp-" + Guid.NewGuid().ToString("N"),
	    Foreground = Brushes.White,
	    Margin = new Thickness(6, 0, 12, 0),
	    FontSize = 13,
	    FontFamily = DefaultFontFamily,
	    VerticalAlignment = VerticalAlignment.Center
	  };
	}
	
	/// <summary>
	/// Creates a simple 2-column form grid (labels left, inputs right).
	/// </summary>
	public static Grid CreateFormGrid(double labelMinWidth = 160)
	{
	  var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
	  grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = labelMinWidth });
	  grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
	  return grid;
	}
	
	/// <summary>
	/// Adds a labeled numeric TextBox row to the provided form grid (next available row).
	/// Returns the TextBox so callers can add events if needed.
	/// </summary>
	public static TextBox AddLabeledNumericRow(Grid grid, string label, string initialText)
	{
	  int rowIndex = grid.RowDefinitions.Count;
	  grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
	
	  var lbl = CreateSettingLabel(label, new Thickness(0, 8, 12, 0));
	  Grid.SetColumn(lbl, 0);
	  Grid.SetRow(lbl, rowIndex);
	  grid.Children.Add(lbl);
	
	  var tb = CreateNumericTextBox(140);
	  tb.Margin = new Thickness(0, 6, 0, 0);
	  tb.Text = initialText;
	  Grid.SetColumn(tb, 1);
	  Grid.SetRow(tb, rowIndex);
	  grid.Children.Add(tb);
	
	  return tb;
	}

  }
}



