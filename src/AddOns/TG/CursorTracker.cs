#region Using declarations
using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Xml;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
#endregion



#region NOTES
/*
Updates:
07/25/2018
- minor bug fix:  object instance error when mouse is over chart while NT8 or workspace is still loading.
- minor bug fix:  System.MissingMethodException logged at load of NT, special thanks to AntiqueBrass for not only pointing this out, but for also supplying a fix!

05/04/2018
- Added option to make the inner tracker filled/hollow.
- Added ability to select/change inner tracker opacity.


As someone who leaves multiple chart windows open across multiple monitors, 
I tend to lose track of where the cursor is, sometimes due to global cross hair looking the same everywhere, 
sometimes due to any number of reasons that would cause the cursor to "jump" to a different position.
This add-on script was written to help me keep track of where the cursor is and not confuse
which chart I'm actually pointing at/with.


There are 2 types of trackers:
The "Outer" tracker creates a border around the chartpanel that the mouse is over.
The "Inner" tracker follows the mouse position within the chartpanel that the mouse is over.

In the "Tools" menu of the Control Center, I've added a "TG" menu. In it, you will find
the "Track Your Cursor" submenu.  "Outer" and "Inner" trackers can be toggled on/off by clicking 
on their respective menu items.
A third menu item "Other Options" opens a window that allows editing of: 
- The thickness of each tracker
- The fade/blur/glow radius of the "Outer" tracker
- The color used on the trackers

Additionally, the "Inner" tracker can dynamically change size 
depending on how much the chart is zoomed in.  This was done to help 
reduce the tracker from impeding on getting a clear view of the chart.

The settings/configurations of this AddOn script are saved to a file 
so that it does not reset back to default every time you restart NT.


Known Issues:
03/23/2018
Fixed missing reference issue.

03/17/2018
There is one known issue...
When one of the trackers has already been toggled off, and you go to
toggle the other off as well, if its menu item is directly over a
chartpanel at the time of clicking on it, its tracker gets "stuck"
on that chartpanel.  It can be cleared by hitting "F5" on the keyboard, 
once you have made sure that the chartpanel's window is the active window.
(or right click on the chart and select "Reload NinjaScript")


I not only welcome, I encourage 
any code fixes that would make this script work more efficiently. 
Please share on the NT support forum, and thank you in advance for any help you may be able to offer.


this script was Written by "Gubbar924" on the NT support forum
http://ninjatrader.com/support/forum/member.php?u=30178
*/
#endregion



namespace NinjaTrader.NinjaScript.AddOns.TG
{



    #region CursorTracker class
    public class CursorTracker : NinjaTrader.NinjaScript.AddOnBase
	{
		
		#region class variables
		private static	UserSettings					trackerSettings;
		
		private			string			    			mySettingsFolder			= NinjaTrader.Core.Globals.UserDataDir + @"bin\Custom\AddOns\TG\CursorTracker\", 
														mySettingsFile				= @"CursorTracker.xml";
		
		private static	bool							OuterTrackerEnabled, 
														InnerTrackerEnabled;
		
		private static double							OuterThickness, 
														OuterBlurRadius, 
														InnerThickness;
		
		
		
		private static	NTMenuItem 						ControlCenterToolsMenu, 
														TGToolsMenuItem, 
														TrackerMainMenuItem, 
														OuterTrackerMenuItem, 
														InnerTrackerMenuItem, 
														OptionsTrackerMenuItem;
		
		private static	Rectangle 						rect1, rect2;
		private static	Ellipse							elps1;
		
		private static	Brush							fillColor					= Brushes.Transparent;
		private	static	Color							dsColor						= Colors.SaddleBrown;
		
		private	static	Point							mousePosition;
		private			double							left, right, top, bottom;
		private			int								ticker;
		#endregion
		
		
		#region OnStateChange()
		protected override void OnStateChange()
		{
			try
			{
				#region State.SetDefaults
				if (State == State.SetDefaults)
				{
					Description									= String.Format(@"A tool to visually help you keep track of where your cursor is by highlighting the chart panel that your cursor is currently over.{0}{0}This script was Written by ""Gubbar924""{0}http://ninjatrader.com/support/forum/member.php?u=30178", Environment.NewLine);
					Name										= "Cursor Tracker";
				}
				#endregion
				
				#region State.Active
				if (State == State.Active)
				{
					Application.Current.Dispatcher.Invoke((Action)(() =>
					{
						if(trackerSettings == null)
						{
							trackerSettings = new UserSettings(mySettingsFolder, mySettingsFile);
						}
						OuterTrackerEnabled = trackerSettings.GetOuterValue();
						InnerTrackerEnabled = trackerSettings.GetInnerValue();
					}));
				}
				#endregion
			}
			catch(Exception ex)
			{
				Print(String.Format("{0}.{1} threw an [{2}] \n\t {3} \n\t {4} \r\n ", this.GetType().Name, MethodBase.GetCurrentMethod().Name, ex.GetType(), ex.Message, ex.StackTrace));
			}
		}
		#endregion
		
		
		#region Group: Windows
		
		#region OnWindowCreated()
		protected override void OnWindowCreated(Window window)
		{
            try
            {
				ControlCenter	cc 	    					= window as ControlCenter;
				Chart 			chartWindow 				= window as Chart;
				
				#region add menuitems to Control Center window
				if(cc != null)
				{
					ControlCenterToolsMenu  				= cc.FindFirst("ControlCenterMenuItemTools") as NTMenuItem;
					
					if(ControlCenterToolsMenu != null)
					{
						if((cc.FindFirst("TGToolsMenuItem") as NTMenuItem) == null)
						{
							TGToolsMenuItem 				= new NTMenuItem { Header = "TG", Style = Application.Current.TryFindResource("MainMenuItem") as Style, IsCheckable = false, IsChecked = false };
							TGToolsMenuItem.SetValue(AutomationProperties.AutomationIdProperty, "TGToolsMenuItem");
							ControlCenterToolsMenu.Items.Add(TGToolsMenuItem);
						}
						else
						{
							TGToolsMenuItem 				= cc.FindFirst("TGToolsMenuItem") as NTMenuItem;
						}
					}
					
					if(TGToolsMenuItem != null)
					{
						NTMenuItem tempTMMI		       		= TGToolsMenuItem.Items.OfType<NTMenuItem>().FirstOrDefault((i) => i.Header == "Track Your Cursor");
						if(tempTMMI == null)
						{
							TrackerMainMenuItem 			= new NTMenuItem { Header = "Track Your Cursor", Style = Application.Current.TryFindResource("MainMenuItem") as Style, IsCheckable = false, IsChecked = false };
							TGToolsMenuItem.Items.Add(TrackerMainMenuItem);
						}
						else
						{
							TrackerMainMenuItem				= tempTMMI;
						}
					}
					
					if(TrackerMainMenuItem != null)
					{
						NTMenuItem tempOTMI		       		= TrackerMainMenuItem.Items.OfType<NTMenuItem>().FirstOrDefault((i) => i.Header == "Outer Tracker");
						NTMenuItem tempITMI		       		= TrackerMainMenuItem.Items.OfType<NTMenuItem>().FirstOrDefault((i) => i.Header == "Inner Tracker");
						NTMenuItem tempCOMI		       		= TrackerMainMenuItem.Items.OfType<NTMenuItem>().FirstOrDefault((i) => i.Header == "Other Options");
						
						if(tempOTMI == null)
						{
							OuterTrackerMenuItem 		 	= new NTMenuItem { Header = "Outer Tracker", Style = Application.Current.TryFindResource("MainMenuItem") as Style, IsCheckable = true, IsChecked = OuterTrackerEnabled };
							OuterTrackerMenuItem.Click     += ToggleOuterTracker;
							TrackerMainMenuItem.Items.Add(OuterTrackerMenuItem);
						}
						else
						{
							OuterTrackerMenuItem			= tempOTMI;
						}
						
						if(tempITMI == null)
						{
							InnerTrackerMenuItem 		 	= new NTMenuItem { Header = "Inner Tracker", Style = Application.Current.TryFindResource("MainMenuItem") as Style, IsCheckable = true, IsChecked = InnerTrackerEnabled };
							InnerTrackerMenuItem.Click     += ToggleInnerTracker;
							TrackerMainMenuItem.Items.Add(InnerTrackerMenuItem);
						}
						else
						{
							InnerTrackerMenuItem			= tempITMI;
						}
						
						if(tempCOMI == null)
						{
							OptionsTrackerMenuItem 		 	= new NTMenuItem { Header = "Other Options", Style = Application.Current.TryFindResource("MainMenuItem") as Style, IsCheckable = false, IsChecked = false };
							OptionsTrackerMenuItem.Click   += EditOptions;
							TrackerMainMenuItem.Items.Add(OptionsTrackerMenuItem);
						}
						else
						{
							OptionsTrackerMenuItem			= tempCOMI;
						}
					}
				}
				#endregion
				
				#region initial setup of each chart window that is opened
				if(chartWindow != null)
				{
					chartWindow.MouseEnter				   += OnWindowMouseEnter;
				}
				#endregion
			}
			catch(Exception ex)
			{
				Print(String.Format("{0}.{1} threw an [{2}] \n\t {3} \n\t {4} \r\n ", this.GetType().Name, MethodBase.GetCurrentMethod().Name, ex.GetType(), ex.Message, ex.StackTrace));
			}
		}
		#endregion
		
		#region OnWindowDestroyed()
		protected override void OnWindowDestroyed(Window window)
		{
			try
			{
				//	remove all subscriptions
				Chart chartWindow = window as Chart;
				if(chartWindow != null)
				{
					chartWindow.MouseEnter				   -= OnWindowMouseEnter;
					
					if(chartWindow.MainTabControl != null)
					{
						foreach(TabItem tab in chartWindow.MainTabControl.Items)
						{
							if(tab != null)
							{
								foreach(ChartPanel cp in (tab.Content as ChartTab).ChartControl.ChartPanels)
								{
									if(cp != null)
									{
										cp.Dispatcher.InvokeAsync((Action)(() =>
										{
											RemoveSubscriptions(cp);
										}));
									}
								}
							}
						}
					}
				}
				
				//	remove menuitems from ControlCenter
				if(window is ControlCenter && TrackerMainMenuItem != null)
				{
					if(ControlCenterToolsMenu != null)
					{
						if(ControlCenterToolsMenu.Items.Contains(TGToolsMenuItem))
						{
							if(TGToolsMenuItem.Items.Contains(TrackerMainMenuItem))
							{
								if(TrackerMainMenuItem.Items.Contains(OuterTrackerMenuItem))
								{
									OuterTrackerMenuItem.Click     -= ToggleOuterTracker;
									TrackerMainMenuItem.Items.Remove(OuterTrackerMenuItem);
								}
								if(TrackerMainMenuItem.Items.Contains(InnerTrackerMenuItem))
								{
									InnerTrackerMenuItem.Click     -= ToggleInnerTracker;
									TrackerMainMenuItem.Items.Remove(InnerTrackerMenuItem);
								}
								if(TrackerMainMenuItem.Items.Contains(OptionsTrackerMenuItem))
								{
									OptionsTrackerMenuItem.Click   -= EditOptions;
									TrackerMainMenuItem.Items.Remove(OptionsTrackerMenuItem);
								}
								
								TGToolsMenuItem.Items.Remove(TrackerMainMenuItem);
							}
							
							if(TGToolsMenuItem.Items.IsEmpty)
							{
								ControlCenterToolsMenu.Items.Remove(TGToolsMenuItem);
							}
						}
					}
				}
			}
			catch(Exception ex)
			{
				Print(String.Format("{0}.{1} threw an [{2}] \n\t {3} \n\t {4} \r\n ", this.GetType().Name, MethodBase.GetCurrentMethod().Name, ex.GetType(), ex.Message, ex.StackTrace));
			}
		}
		#endregion
		
		#endregion
		
		
		#region Group: MenuItems
		
		//	for the "Outer Tracker" menu item
		#region ToggleOuterTracker()
		private void ToggleOuterTracker(object sender, RoutedEventArgs e)
		{
			try
			{
				NTMenuItem s = sender as NTMenuItem;
					
				if(s != null && e != null)
				{
					OuterTrackerEnabled 	= !OuterTrackerEnabled;
					trackerSettings.SetOuterValue(OuterTrackerEnabled);
					s.IsChecked 			= OuterTrackerEnabled;
					UpdateAllPanels();
				}
			}
			catch(Exception ex)
			{
				Print(String.Format("{0}.{1} threw an [{2}] \n\t {3} \n\t {4} \r\n ", this.GetType().Name, MethodBase.GetCurrentMethod().Name, ex.GetType(), ex.Message, ex.StackTrace));
			}
		}
		#endregion
		
		//	for the "Inner Tracker" menu item
		#region ToggleInnerTracker()
		private void ToggleInnerTracker(object sender, RoutedEventArgs e)
		{
			try
			{
				NTMenuItem s = sender as NTMenuItem;
					
				if(s != null && e != null)
				{
					InnerTrackerEnabled 	= !InnerTrackerEnabled;
					trackerSettings.SetInnerValue(InnerTrackerEnabled);
					s.IsChecked 			= InnerTrackerEnabled;
					UpdateAllPanels();
				}
			}
			catch(Exception ex)
			{
				Print(String.Format("{0}.{1} threw an [{2}] \n\t {3} \n\t {4} \r\n ", this.GetType().Name, MethodBase.GetCurrentMethod().Name, ex.GetType(), ex.Message, ex.StackTrace));
			}
		}
		#endregion
		
		//	for the "Other Options" menu item
		#region EditOptions()
		private void EditOptions(object sender, RoutedEventArgs e)
		{
			try
			{
				NTMenuItem s = sender as NTMenuItem;
					
				if(s != null && e != null)
				{
					Application.Current.Dispatcher.InvokeAsync((Action)(() =>
					{
						trackerSettings.ShowOptions();
					}));
				}
			}
			catch(Exception ex)
			{
				Print(String.Format("{0}.{1} threw an [{2}] \n\t {3} \n\t {4} \r\n ", this.GetType().Name, MethodBase.GetCurrentMethod().Name, ex.GetType(), ex.Message, ex.StackTrace));
			}
		}
		#endregion
		
		//	The main chartpanel updater called by the menuitems
		#region UpdateAllPanels()
		private void UpdateAllPanels()
		{
			try
			{
				foreach(Window win in NinjaTrader.Core.Globals.AllWindows)
				{
					if(win is NTWindow || win is Chart)
					{
                        Chart chartWindow = win as Chart;
						
						if(chartWindow != null)
						{
							TabControl tabControl = chartWindow.MainTabControl;
							
							if(tabControl != null)
							{
								foreach(TabItem tab in tabControl.Items)
								{
									if(tab != null)
									{
										tab.Dispatcher.InvokeAsync(() =>
										{
											foreach(ChartPanel cp in (tab.Content as ChartTab).ChartControl.ChartPanels)
											{
												if(cp != null)
												{
													CheckSubscriptions(cp);
												}
											}
										});
									}
								}
							}
						}
					}
				}
			}
			catch(Exception ex)
			{
				Print(String.Format("{0}.{1} threw an [{2}] \n\t {3} \n\t {4} \r\n ", this.GetType().Name, MethodBase.GetCurrentMethod().Name, ex.GetType(), ex.Message, ex.StackTrace));
			}
		}
		#endregion
		
		#endregion
		
		
		#region Group: Handlers
		
		//	There are no events that are specifically triggered when a chart template is loaded
		//	but loading a template breaks functionality of this script on the tab on which the template is loaded
		//	so we add a check during Window MouseEnter to circumvent the issue
		#region OnWindowMouseEnter()
		private void OnWindowMouseEnter(object sender, MouseEventArgs e)
		{
			try
            {
				if(sender != null && e != null)
				{
					Chart   	chartWindow				= sender as Chart;
					TabControl	tabControl				= chartWindow.MainTabControl;
					
					if(chartWindow != null && tabControl != null)
					{
						foreach(TabItem tab in tabControl.Items)
						{
							if(tab != null && tab == tabControl.Items[tabControl.SelectedIndex])
							{
								foreach(ChartPanel cp in (tab.Content as ChartTab).ChartControl.ChartPanels)
								{
									if(cp != null)
									{
										CheckSubscriptions(cp);
									}
								}
							}
						}
					}
				}
			}
			catch(Exception ex)
			{
				Print(String.Format("{0}.{1} threw an [{2}] \n\t {3} \n\t {4} \r\n ", this.GetType().Name, MethodBase.GetCurrentMethod().Name, ex.GetType(), ex.Message, ex.StackTrace));
			}
        }
		#endregion
		
		//	There is no INotifyPropertyChanged for chartpanels being added/removed
		//	Since adding/removing them would change their sizes anyway, We use SizeChange to emulate add/remove
		//	this makes sure any new chartpanels added also get "highlighted"
		#region OnSizeChange()
		private void OnSizeChange(object sender, SizeChangedEventArgs e)
		{
			try
            {
				if(sender != null && e != null)
				{
					ChartPanel		scp						= sender as ChartPanel;
					ChartControl	cc						= scp.ChartControl;
					Chart 			chartWindow				= Window.GetWindow(cc.Parent) as Chart;
					
					if(chartWindow != null && chartWindow.MainTabControl != null)
					{
						TabControl tabControl = chartWindow.MainTabControl;
						
						if(tabControl.SelectedIndex >= 0)
						{
							TabItem tab = tabControl.Items[tabControl.SelectedIndex] as TabItem;
							
							if(tab != null)
							{
								foreach(ChartPanel cp in (tab.Content as ChartTab).ChartControl.ChartPanels)
								{
									if(cp != null)
									{
										CheckSubscriptions(cp);
									}
								}
							}
						}
					}
				}
            }
            catch(Exception ex)
			{
				Print(String.Format("{0}.{1} threw an [{2}] \n\t {3} \n\t {4} \r\n ", this.GetType().Name, MethodBase.GetCurrentMethod().Name, ex.GetType(), ex.Message, ex.StackTrace));
			}
		}
		#endregion
		
		
		#region Group: Mouse Handlers for Panels
		
		//	add shapes
		#region OnMouseEnter()
		private void OnMouseEnter(object sender, MouseEventArgs e)
		{
			try
            {
				if(TrackerMainMenuItem == null)
				{
					return;
				}
				
				if(!TrackerMainMenuItem.IsVisible && (InnerTrackerEnabled || OuterTrackerEnabled) && sender != null && e != null)
				{
					ChartPanel   cp 						= sender as ChartPanel;
					ChartControl cc							= cp.ChartControl;
								 ticker						= 0;
				
					cp.Dispatcher.Invoke((Action)(() =>
					{
						//	these 4 values are defined here due to thread access issues
						//	if you know how to recode this to be more resource friendly
						//	it would be appreciated if you could share it with me and/or the NT community
						double	 OuterThickness				= trackerSettings.GetOuterThickness(), 
								 OuterBlurRadius			= trackerSettings.GetOuterBlurRadius(), 
								 InnerThickness				= trackerSettings.GetInnerThickness();
						Brush	 highlightColor				= trackerSettings.GetHighLightColor();
						
						
						// Get ChartPanel bounds relative to the ChartControl (ALL IN WPF DIPs)
						Point pt = cp.TransformToAncestor(cc).Transform(new Point(0, 0));
						
						double left = pt.X;
						double top  = pt.Y;
						
						// Use ActualWidth/ActualHeight in DIPs
						double cpw  = cp.ActualWidth  + cc.Properties.PanelSplitterPen.Width; // optional
						double cph  = cp.ActualHeight;
						
						// Optional: size the canvas so it’s properly arranged
						Canvas canvass = new Canvas();
						canvass.Name = "canvass";

						if(OuterTrackerEnabled)
						{
							rect1 							= new Rectangle();
							rect1.StrokeThickness			= OuterThickness * 2;
							rect1.Stroke 					= highlightColor;
							rect1.Fill 						= fillColor;
							rect1.Effect					= new BlurEffect() { Radius = OuterBlurRadius };
//							rect1.Width						= cpw;
//							rect1.Height					= cph;
							
							rect1.Width  = cpw;
							rect1.Height = cph;
							Canvas.SetLeft(rect1, left);
							Canvas.SetTop(rect1, top);
							
							canvass.Children.Add(rect1);
							Canvas.SetLeft(rect1, left);
							Canvas.SetTop(rect1, top);
						
							rect2 							= new Rectangle();
							rect2.StrokeThickness			= OuterThickness;
							rect2.Stroke 					= highlightColor;
							rect2.Fill 						= fillColor;
//							rect2.Width						= cpw;
//							rect2.Height					= cph;
							
							rect2.Width  = cpw;
							rect2.Height = cph;
							Canvas.SetLeft(rect2, left);
							Canvas.SetTop(rect2, top);
							
							canvass.Children.Add(rect2);
							Canvas.SetLeft(rect2, left);
							Canvas.SetTop(rect2, top);
							
						}					
						if(InnerTrackerEnabled)
						{
							bool   InnerTrackerFilled  		= trackerSettings.GetInnerFilled();
							Brush  innerColor				= highlightColor.Clone();
								   innerColor.Opacity		= trackerSettings.GetColorOpacity();
							double spaced					= cc.BarWidth + cc.Properties.BarDistance;
							double thisMin					= Math.Min(InnerThickness * 15, Math.Max(spaced, InnerThickness * 15));
							double thisMax					= Math.Min(cpw, cph);
							double useThis					= Math.Max(Math.Min(spaced, thisMax), thisMin);
							elps1							= new Ellipse();
							elps1.StrokeThickness			= InnerThickness;
							elps1.Stroke 					= innerColor;
							//	if the outer ring is still bothering you, you can comment out the previous line and uncomment the next line
//							elps1.Stroke 					= InnerTrackerFilled ? fillColor : innerColor;
							elps1.Fill 						= InnerTrackerFilled ? innerColor : fillColor;
							elps1.Width						= useThis;
							elps1.Height					= useThis;
							if(!InnerTrackerFilled)
								elps1.Effect				= new DropShadowEffect() { Direction = 360 - (25 * InnerThickness), ShadowDepth = InnerThickness * 2, Color = dsColor, Opacity = InnerThickness * .5 > 1 ? 1 : InnerThickness < 0 ? 0 : InnerThickness * .5, BlurRadius = InnerThickness * 4 };
							canvass.Children.Add(elps1);
							Canvas.SetLeft(elps1, left);
							Canvas.SetTop(elps1, top);
						}

						cc.Children.Add(canvass);
						Panel.SetZIndex(canvass, int.MinValue);
						

						cc.InvalidateVisual();
						
					}));
					
					

				}
            }
            catch(Exception ex)
			{
				Print(String.Format("{0}.{1} threw an [{2}] \n\t {3} \n\t {4} \r\n ", this.GetType().Name, MethodBase.GetCurrentMethod().Name, ex.GetType(), ex.Message, ex.StackTrace));
			}
		}
		#endregion
		
		//	manage shapes
		#region OnMouseMove()
		private void OnMouseMove(object sender, MouseEventArgs e)
        {
            try
            {
				if(!InnerTrackerEnabled)
				{
					return;
				}
				
				if(sender != null && e != null && elps1 != null && mousePosition != null)
				{
					if(ticker < 2 || ticker % 5 == 0)			//	accept the initial mouse move that enters a chartpanel, after that try to reduce cpu usage
					{
						ChartPanel   cp 						= sender as ChartPanel;
						ChartControl cc							= cp.ChartControl;
					
						mousePosition 							= e.GetPosition(cc);
//						mousePosition.X   						= ChartingExtensions.ConvertToHorizontalPixels(mousePosition.X, cc.PresentationSource);
//						mousePosition.Y							= ChartingExtensions.ConvertToVerticalPixels(mousePosition.Y, cc.PresentationSource);
					
						elps1.Dispatcher.Invoke((Action)(() =>
						{
							elps1.SetValue(Canvas.LeftProperty, mousePosition.X - (elps1.ActualWidth / 2));
		                    elps1.SetValue(Canvas.TopProperty, mousePosition.Y - (elps1.ActualHeight / 2));
						}));
						
						ticker++;
					}
					else
					{
						ticker++;
					}
				}
            }
            catch(Exception ex)
			{
				Print(String.Format("{0}.{1} threw an [{2}] \n\t {3} \n\t {4} \r\n ", this.GetType().Name, MethodBase.GetCurrentMethod().Name, ex.GetType(), ex.Message, ex.StackTrace));
			}
        }
		#endregion
		
		//	remove shapes
		#region OnMouseLeave()
		private void OnMouseLeave(object sender, MouseEventArgs e)
		{
			try
            {
				if((OuterTrackerEnabled || InnerTrackerEnabled) && sender != null && e != null)
				{
					ChartPanel   cp							= sender as ChartPanel;
					ChartControl cc							= cp.ChartControl;
					
					
					if(cc.Children.OfType<Canvas>().Any((cv) => cv.Name == "canvass"))
					{
						Canvas 	 canvass					= (Canvas)cc.Children.OfType<Canvas>().FirstOrDefault((cv) => cv.Name == "canvass");
						canvass.Children.Clear();
						cc.Children.Remove(canvass);
					}
					
					cc.InvalidateVisual();
				}
            }
            catch(Exception ex)
			{
				Print(String.Format("{0}.{1} threw an [{2}] \n\t {3} \n\t {4} \r\n ", this.GetType().Name, MethodBase.GetCurrentMethod().Name, ex.GetType(), ex.Message, ex.StackTrace));
			}
		}
        #endregion

        #endregion
		
		
		#region Group: Subscriptions
		
		#region CheckSubscriptions()
		private void CheckSubscriptions(ChartPanel cp)
		{
			try
			{
				cp.Dispatcher.InvokeAsync((Action)(() =>
				{
					if(OuterTrackerEnabled || InnerTrackerEnabled)
					{
						AddSubscriptions(cp);
					}
					else
					{
						RemoveSubscriptions(cp);
					}
				}));
			}
			catch(Exception ex)
			{
				Print(String.Format("{0}.{1} threw an [{2}] \n\t {3} \n\t {4} \r\n ", this.GetType().Name, MethodBase.GetCurrentMethod().Name, ex.GetType(), ex.Message, ex.StackTrace));
			}
		}
		#endregion
		
		#region AddSubscriptions()
		private void AddSubscriptions(ChartPanel cp)
		{
			//	remove any lingering subscriptions first
			RemoveSubscriptions(cp);
			
			cp.MouseMove   		+= OnMouseMove;
			cp.MouseEnter  		+= OnMouseEnter;
			cp.MouseLeave  		+= OnMouseLeave;
			cp.SizeChanged 		+= OnSizeChange;
		}
		#endregion
		
		#region RemoveSubscriptions()
		private void RemoveSubscriptions(ChartPanel cp)
		{
			cp.MouseMove   		-= OnMouseMove;
			cp.MouseEnter  		-= OnMouseEnter;
			cp.MouseLeave  		-= OnMouseLeave;
			cp.SizeChanged 		-= OnSizeChange;
		}
		#endregion
		
		#endregion
		
		#endregion


    }
	#endregion
	
	
	
	#region UserSettings class
	public class UserSettings : NinjaTrader.NinjaScript.AddOnBase
	{
		
		
		#region class variables
		private const 	string 	DefaultVals		= "<Trackers><Outer><Enabled>true</Enabled><Thickness>2.00</Thickness><Radius>15.00</Radius></Outer><Inner><Enabled>true</Enabled><Thickness>2.00</Thickness><Filled>false</Filled></Inner><Color>#FFFFA500</Color><Opacity>1.00</Opacity></Trackers>";
		private			string	SettingsFile;
		
		//	even though these are doubles, we will be incrementing them as integers in the comboboxes of the options window
		//	so that it does not cause any hang ups or hiccups in NT
		public			double	MaxThickness	= 25.00, 
								MinThickness	= 1.00, 
								MaxRadius		= 100.00, 
								MinRadius		= 1.00, 
								MaxOpacity		= 1.00, 
								MinOpacity		= 0.01;
		#endregion
		
		
		#region constructor
		public UserSettings() : this (" ", " ") {} // default constructor

		public UserSettings(string saveDir, string saveFile)
		{
			if(saveDir != " ")
				this.SettingsFile 	= GetFile(saveDir, saveFile);
		}
		#endregion
		
		
		#region GetFile()
		private string GetFile(string saveDir, string saveFile)
		{
			string sfn = string.Concat(saveDir, saveFile);
			
			if( !System.IO.Directory.Exists(saveDir) )
			{
				System.IO.Directory.CreateDirectory(saveDir);
			}
			
			if( !System.IO.File.Exists(sfn) )
			{
				XmlDocument xmlDoc 				= new XmlDocument();
				xmlDoc.LoadXml(DefaultVals);
				XmlTextWriter writer 			= new XmlTextWriter(sfn, null);
				writer.Formatting 				= Formatting.Indented;
				xmlDoc.Save(writer);
				writer.Close();
			}
			
			return sfn;
		}
		#endregion
		
		
		#region ShowOptions()
		public void ShowOptions()
		{
			CursorTrackerEditor CTE = new CursorTrackerEditor(this);
			CTE.Topmost				= true;
			CTE.ShowDialog();
		}
		#endregion
		
		
		#region GetOuterValue()
		public bool GetOuterValue()
		{
			XmlDocument xmlDoc 				= new XmlDocument();
			xmlDoc.Load(SettingsFile);
			
	        XmlNode 	outerNode  			= xmlDoc.SelectSingleNode("//Trackers/Outer/Enabled");
			
			return Convert.ToBoolean(outerNode.InnerText);
		}
		#endregion
		
		
		#region SetOuterValue()
		public void SetOuterValue(bool newOuterVal)
		{
			XmlDocument xmlDoc 				= new XmlDocument();
			xmlDoc.Load(SettingsFile);
            
			XmlNode 	outerNode  			= xmlDoc.SelectSingleNode("//Trackers/Outer/Enabled");
			outerNode.InnerText				= newOuterVal.ToString().ToLower();
			
			xmlDoc.Save(SettingsFile);
		}
		#endregion
		
		
		#region GetOuterThickness()
		public double GetOuterThickness()
		{
			XmlDocument xmlDoc 				= new XmlDocument();
			xmlDoc.Load(SettingsFile);
			
	        XmlNode 	oThickNode  		= xmlDoc.SelectSingleNode("//Trackers/Outer/Thickness");
			
			double		tmpVal				= Convert.ToDouble(oThickNode.InnerText);
						tmpVal				= tmpVal > MaxThickness ? MaxThickness : tmpVal < MinThickness ? MinThickness : tmpVal;
			
			return tmpVal;
		}
		#endregion
		
		
		#region SetOuterThickness()
		public void SetOuterThickness(double newOuterThickness)
		{
			XmlDocument xmlDoc 				= new XmlDocument();
			xmlDoc.Load(SettingsFile);
			
	        XmlNode 	oThickNode  		= xmlDoc.SelectSingleNode("//Trackers/Outer/Thickness");
			oThickNode.InnerText			= newOuterThickness > MaxThickness ? MaxThickness.ToString("0.00") : newOuterThickness < MinThickness ? MinThickness.ToString("0.00") : newOuterThickness.ToString("0.00");
			
			xmlDoc.Save(SettingsFile);
		}
		#endregion
		
		
		#region GetOuterBlurRadius()
		public double GetOuterBlurRadius()
		{
			XmlDocument xmlDoc 				= new XmlDocument();
			xmlDoc.Load(SettingsFile);
			
	        XmlNode 	oRadiusNode  		= xmlDoc.SelectSingleNode("//Trackers/Outer/Radius");
			
			double		tmpVal				= Convert.ToDouble(oRadiusNode.InnerText);
						tmpVal				= tmpVal > MaxRadius ? MaxRadius : tmpVal < MinRadius ? MinRadius : tmpVal;
			
			return tmpVal;
		}
		#endregion
		
		
		#region SetOuterBlurRadius()
		public void SetOuterBlurRadius(double newOuterRadius)
		{
			XmlDocument xmlDoc 				= new XmlDocument();
			xmlDoc.Load(SettingsFile);
			
	        XmlNode 	oRadiusNode  		= xmlDoc.SelectSingleNode("//Trackers/Outer/Radius");
			oRadiusNode.InnerText			= newOuterRadius > MaxRadius ? MaxRadius.ToString("0.00") : newOuterRadius < MinRadius ? MinRadius.ToString("0.00") : newOuterRadius.ToString("0.00");
			
			xmlDoc.Save(SettingsFile);
		}
		#endregion
		
		
		#region GetInnerValue()
		public bool GetInnerValue()
		{
			XmlDocument xmlDoc 				= new XmlDocument();
			xmlDoc.Load(SettingsFile);
			
            XmlNode 	innerNode  			= xmlDoc.SelectSingleNode("//Trackers/Inner/Enabled");
			
			return Convert.ToBoolean(innerNode.InnerText);
		}
		#endregion
		
		
		#region SetInnerValue()
		public void SetInnerValue(bool newInnerVal)
		{
			XmlDocument xmlDoc 				= new XmlDocument();
			xmlDoc.Load(SettingsFile);
           
			XmlNode 	innerNode  			= xmlDoc.SelectSingleNode("//Trackers/Inner/Enabled");
			innerNode.InnerText				= newInnerVal.ToString().ToLower();
			
			xmlDoc.Save(SettingsFile);
		}
		#endregion
		
		
		#region GetInnerThickness()
		public double GetInnerThickness()
		{
			XmlDocument xmlDoc 				= new XmlDocument();
			xmlDoc.Load(SettingsFile);
			
	        XmlNode 	iThickNode  		= xmlDoc.SelectSingleNode("//Trackers/Inner/Thickness");
			
			double		tmpVal				= Convert.ToDouble(iThickNode.InnerText);
						tmpVal				= tmpVal > MaxThickness ? MaxThickness : tmpVal < MinThickness ? MinThickness : tmpVal;
			
			return tmpVal;
		}
		#endregion
		
		
		#region SetInnerThickness()
		public void SetInnerThickness(double newInnerThickness)
		{
			XmlDocument xmlDoc 				= new XmlDocument();
			xmlDoc.Load(SettingsFile);
			
	        XmlNode 	iThickNode  		= xmlDoc.SelectSingleNode("//Trackers/Inner/Thickness");
			iThickNode.InnerText			= newInnerThickness > MaxThickness ? MaxThickness.ToString("0.00") : newInnerThickness < MinThickness ? MinThickness.ToString("0.00") : newInnerThickness.ToString("0.00");
			
			xmlDoc.Save(SettingsFile);
		}
		#endregion
		
		
		#region GetHighLightColor()
		public Brush GetHighLightColor()
		{
			XmlDocument xmlDoc 				= new XmlDocument();
			xmlDoc.Load(SettingsFile);
			
            XmlNode 	colorNode  			= xmlDoc.SelectSingleNode("//Trackers/Color");
			try
			{
				return (Brush)(new BrushConverter().ConvertFromString(colorNode.InnerText));
			}
            catch(Exception ex)
			{
				return (Brush)(typeof(Brushes).GetProperty(colorNode.InnerText) as PropertyInfo).GetValue(null, null);
				Print(String.Format("{0}.{1} threw an [{2}] \n\t {3} \n\t {4} \r\n ", this.GetType().Name, MethodBase.GetCurrentMethod().Name, ex.GetType(), ex.Message, ex.StackTrace));
			}
		}
		#endregion
		
		
		#region SetHighLightColor()
		public void SetHighLightColor(Brush newBrush)
		{
			XmlDocument xmlDoc 				= new XmlDocument();
			xmlDoc.Load(SettingsFile);
           
			XmlNode 	colorNode  			= xmlDoc.SelectSingleNode("//Trackers/Color");
			colorNode.InnerText				= (((SolidColorBrush)newBrush).Color).ToString();
			xmlDoc.Save(SettingsFile);
		}
		#endregion
		
		
		#region GetColorOpacity()
		public double GetColorOpacity()
		{
			XmlDocument xmlDoc 				= new XmlDocument();
			xmlDoc.Load(SettingsFile);
			
	        XmlNode 	opacityNode  		= xmlDoc.SelectSingleNode("//Trackers/Opacity");
			
			double		tmpVal				= Convert.ToDouble(opacityNode.InnerText);
						tmpVal				= tmpVal > MaxOpacity ? MaxOpacity : tmpVal < MinOpacity ? MinOpacity : tmpVal;
			
			return tmpVal;
		}
		#endregion
		
		
		#region SetColorOpacity()
		public void SetColorOpacity(double newColorOpacity)
		{
			XmlDocument xmlDoc 				= new XmlDocument();
			xmlDoc.Load(SettingsFile);
			
	        XmlNode 	opacityNode  		= xmlDoc.SelectSingleNode("//Trackers/Opacity");
			opacityNode.InnerText			= newColorOpacity > MaxOpacity ? MaxOpacity.ToString("0.00") : newColorOpacity < MinOpacity ? MinOpacity.ToString("0.00") : newColorOpacity.ToString("0.00");
			
			xmlDoc.Save(SettingsFile);
		}
		#endregion
		
		
		#region GetInnerFilled()
		public bool GetInnerFilled()
		{
			XmlDocument xmlDoc 				= new XmlDocument();
			xmlDoc.Load(SettingsFile);
			
            XmlNode 	innerFillNode		= xmlDoc.SelectSingleNode("//Trackers/Inner/Filled");
			
			return Convert.ToBoolean(innerFillNode.InnerText);
		}
		#endregion
		
		
		#region SetInnerFilled()
		public void SetInnerFilled(bool newInnerFillVal)
		{
			XmlDocument xmlDoc 				= new XmlDocument();
			xmlDoc.Load(SettingsFile);
			
            XmlNode 	innerFillNode		= xmlDoc.SelectSingleNode("//Trackers/Inner/Filled");
			innerFillNode.InnerText			= newInnerFillVal.ToString().ToLower();
			
			xmlDoc.Save(SettingsFile);
		}
		#endregion
		
		
	}
	#endregion
	
	
	
	#region CursorTrackerEditor class
	class CursorTrackerEditor : NTWindow
	{
		
		
		#region constructor
		public CursorTrackerEditor(UserSettings Settings)
		{
			SimpleFont 		myFont	   		= new SimpleFont("Global User Interface", 14) { Size = 14, Italic = true, Bold = false, Family = new FontFamily("Global User Interface") };
			
			#region bpTextBlock
			TextBlock bpTextBlock				= new TextBlock();
			bpTextBlock.Text					= "Color:";
			bpTextBlock.HorizontalAlignment		= HorizontalAlignment.Right;
			bpTextBlock.VerticalAlignment		= VerticalAlignment.Bottom;
			myFont.ApplyTo(bpTextBlock);
			#endregion
			
			#region brushPicker
			System.Windows.Controls.WpfPropertyGrid.Controls.BrushPicker brushPicker 
													= new System.Windows.Controls.WpfPropertyGrid.Controls.BrushPicker();
			brushPicker.SelectedBrush				= Settings.GetHighLightColor();
			#endregion
			
			#region boTextBlock
			TextBlock boTextBlock				= new TextBlock();
			boTextBlock.Text					= "Inner Color Opacity:";
			boTextBlock.HorizontalAlignment		= HorizontalAlignment.Right;
			boTextBlock.VerticalAlignment		= VerticalAlignment.Bottom;
			myFont.ApplyTo(boTextBlock);
			#endregion
			
			#region boComboBox
			ComboBox boComboBox					= new ComboBox();
			PopulateBox(boComboBox, .01, 1.01, Settings.GetColorOpacity(), .01);
			#endregion
			
			#region otTextBlock
			TextBlock otTextBlock				= new TextBlock();
			otTextBlock.Text					= "Outer Thickness:";
			otTextBlock.HorizontalAlignment		= HorizontalAlignment.Right;
			otTextBlock.VerticalAlignment		= VerticalAlignment.Bottom;
			myFont.ApplyTo(otTextBlock);
			#endregion
			
			#region otComboBox
			ComboBox otComboBox					= new ComboBox();
			PopulateBox(otComboBox, Settings.MinThickness, Settings.MaxThickness, Settings.GetOuterThickness());
			#endregion
			
			#region orTextBlock
			TextBlock orTextBlock				= new TextBlock();
			orTextBlock.Text					= "Outer Radius:";
			orTextBlock.HorizontalAlignment		= HorizontalAlignment.Right;
			orTextBlock.VerticalAlignment		= VerticalAlignment.Bottom;
			myFont.ApplyTo(orTextBlock);
			#endregion
			
			#region orComboBox
			ComboBox orComboBox					= new ComboBox();
			PopulateBox(orComboBox, Settings.MinRadius, Settings.MaxRadius, Settings.GetOuterBlurRadius());
			#endregion
			
			#region itTextBlock
			TextBlock itTextBlock				= new TextBlock();
			itTextBlock.Text					= "Inner Thickness:";
			itTextBlock.HorizontalAlignment		= HorizontalAlignment.Right;
			itTextBlock.VerticalAlignment		= VerticalAlignment.Bottom;
			myFont.ApplyTo(itTextBlock);
			#endregion
			
			#region itComboBox
			ComboBox itComboBox					= new ComboBox();
			PopulateBox(itComboBox, Settings.MinThickness, Settings.MaxThickness, Settings.GetInnerThickness());
			#endregion
			
			#region ifTextBlock
			TextBlock ifTextBlock				= new TextBlock();
			ifTextBlock.Text					= "Fill Inner Tracker:";
			ifTextBlock.HorizontalAlignment		= HorizontalAlignment.Right;
			ifTextBlock.VerticalAlignment		= VerticalAlignment.Bottom;
			myFont.ApplyTo(ifTextBlock);
			#endregion
			
			#region ifCheckBox
			CheckBox ifCheckBox					= new CheckBox();
//			ifCheckBox.Content					= "Fill Inner Tracker";
			ifCheckBox.IsChecked				= Settings.GetInnerFilled();
			#endregion
			
			#region applyButton
			Button applyButton 						= new Button();
			applyButton.Content						= "Apply";
			applyButton.HorizontalAlignment			= HorizontalAlignment.Left;
			
			applyButton.Click 					   += (s, e) => {
																	Settings.SetHighLightColor(brushPicker.SelectedBrush);
																	Settings.SetColorOpacity(Convert.ToDouble(boComboBox.SelectedItem));
																	Settings.SetOuterThickness(Convert.ToDouble(otComboBox.SelectedItem));
																	Settings.SetOuterBlurRadius(Convert.ToDouble(orComboBox.SelectedItem));
																	Settings.SetInnerThickness(Convert.ToDouble(itComboBox.SelectedItem));
																	Settings.SetInnerFilled(ifCheckBox.IsChecked ?? false);
																	Close();
            													};
			#endregion
			
			#region cancelButton
			Button cancelButton						= new Button();
			cancelButton.Content					= "Cancel";
			cancelButton.IsCancel					= true;
			cancelButton.HorizontalAlignment		= HorizontalAlignment.Right;
			#endregion
			
			#region itemsGrid
			Grid itemsGrid 							= new Grid();
			itemsGrid.ShowGridLines					= false;
			itemsGrid.Margin						= new Thickness(10);
			
			itemsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
			itemsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10, GridUnitType.Pixel) });
            itemsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
			itemsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10, GridUnitType.Pixel) });
			itemsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
			itemsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10, GridUnitType.Pixel) });
			itemsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
			itemsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10, GridUnitType.Pixel) });
			itemsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
			itemsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10, GridUnitType.Pixel) });
			itemsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
			itemsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10, GridUnitType.Pixel) });
			itemsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
			itemsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
			itemsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10, GridUnitType.Pixel) });
			itemsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
			
			Grid.SetColumn(bpTextBlock, 0);
			Grid.SetRow(bpTextBlock, 0);
			itemsGrid.Children.Add(bpTextBlock);
			
			Grid.SetColumn(brushPicker, 2);
			Grid.SetRow(brushPicker, 0);
			itemsGrid.Children.Add(brushPicker);
			
			Grid.SetColumn(boTextBlock, 0);
			Grid.SetRow(boTextBlock, 2);
			itemsGrid.Children.Add(boTextBlock);
			
			Grid.SetColumn(boComboBox, 2);
			Grid.SetRow(boComboBox, 2);
			itemsGrid.Children.Add(boComboBox);
			
			Grid.SetColumn(otTextBlock, 0);
			Grid.SetRow(otTextBlock, 4);
			itemsGrid.Children.Add(otTextBlock);
			
			Grid.SetColumn(otComboBox, 2);
			Grid.SetRow(otComboBox, 4);
			itemsGrid.Children.Add(otComboBox);
			
			Grid.SetColumn(orTextBlock, 0);
			Grid.SetRow(orTextBlock, 6);
			itemsGrid.Children.Add(orTextBlock);
			
			Grid.SetColumn(orComboBox, 2);
			Grid.SetRow(orComboBox, 6);
			itemsGrid.Children.Add(orComboBox);
			
			Grid.SetColumn(itTextBlock, 0);
			Grid.SetRow(itTextBlock, 8);
			itemsGrid.Children.Add(itTextBlock);
			
			Grid.SetColumn(itComboBox, 2);
			Grid.SetRow(itComboBox, 8);
			itemsGrid.Children.Add(itComboBox);
			
			Grid.SetColumn(ifTextBlock, 0);
			Grid.SetRow(ifTextBlock, 10);
			itemsGrid.Children.Add(ifTextBlock);
			
			Grid.SetColumn(ifCheckBox, 2);
			Grid.SetRow(ifCheckBox, 10);
			itemsGrid.Children.Add(ifCheckBox);
			
			Grid.SetColumn(applyButton, 0);
			Grid.SetRow(applyButton, 12);
			itemsGrid.Children.Add(applyButton);
			
			Grid.SetColumn(cancelButton, 2);
			Grid.SetRow(cancelButton, 12);
			itemsGrid.Children.Add(cancelButton);
			#endregion
			
			#region this
			this.SetValue(AutomationProperties.AutomationIdProperty, "tgCTEwindow");
            Caption 							= "Cursor Tracker Options";
			WindowStartupLocation				= WindowStartupLocation.CenterScreen;
			SizeToContent 						= SizeToContent.WidthAndHeight;
			ResizeMode							= ResizeMode.NoResize;
			Content 							= itemsGrid;
            Width   							= 100;
            Height  							= 100;
			#endregion
		}
		#endregion
		
		
		#region PopulateBox()
		private void PopulateBox(ComboBox box, double thisMin, double thisMax, double thisDefault, double increment = 1)
		{
			string strDefault	= increment == 1 ? thisDefault.ToString("0") : thisDefault.ToString("0.00");
			
			for(double d = thisMin; d <= thisMax; d = d + increment)
            {
                box.Items.Add(newItem: increment == 1 ? d.ToString("0") : d.ToString("0.00"));
            }
			
			box.SelectedItem = box.Items.Contains(strDefault) ? strDefault : box.Items[0];
		}
		#endregion
		
		
	}
	#endregion
	
	
	
}
