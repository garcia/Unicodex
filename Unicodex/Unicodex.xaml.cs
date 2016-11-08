﻿using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Resources;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace Unicodex
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private UnicodexSearch unicodexSearch;

        private ObservableCollection<View.Character> results;

        private System.Windows.Forms.NotifyIcon notifyIcon;

        public MainWindow()
        {
            unicodexSearch = new UnicodexSearch();
            InitializeComponent();
            InitializeNotifyIcon();
        }

        private void InitializeNotifyIcon()
        {
            // Create tray icon
            notifyIcon = new System.Windows.Forms.NotifyIcon();
            notifyIcon.Icon = Properties.Resources.main;
            notifyIcon.Visible = true;
            notifyIcon.DoubleClick += delegate (object sender, EventArgs args)
            {
                Show();
                WindowState = WindowState.Normal;
            };

            // Create right-click context menu for tray icon
            System.Windows.Forms.MenuItem menuItemShow = new System.Windows.Forms.MenuItem();
            menuItemShow.Index = 0;
            menuItemShow.Text = "&Show";
            menuItemShow.Click += new System.EventHandler(menuItemShow_Click);

            System.Windows.Forms.MenuItem menuItemExit = new System.Windows.Forms.MenuItem();
            menuItemExit.Index = 1;
            menuItemExit.Text = "E&xit";
            menuItemExit.Click += new System.EventHandler(menuItemExit_Click);

            System.Windows.Forms.ContextMenu contextMenu = new System.Windows.Forms.ContextMenu();
            contextMenu.MenuItems.AddRange(new System.Windows.Forms.MenuItem[] { menuItemShow, menuItemExit });
            notifyIcon.ContextMenu = contextMenu;
        }

        private void menuItemShow_Click(object sender, EventArgs e)
        {
            Show();
        }

        private void menuItemExit_Click(object sender, EventArgs e)
        {
            Shutdown();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Add WndProc handler
            HwndSource source = PresentationSource.FromVisual(this) as HwndSource;
            source.AddHook(WndProc);

            // Register hotkey (TODO: make user-configurable)
            IntPtr hWnd = getMainWindowHwnd();
            Win32.RegisterHotKey(hWnd, 0, Win32.MOD_NOREPEAT | Win32.MOD_CONTROL | Win32.MOD_SHIFT, KeyInterop.VirtualKeyFromKey(Key.U));
        }

        private IntPtr getMainWindowHwnd()
        {
            return (IntPtr)new WindowInteropHelper(Application.Current.MainWindow).Handle.ToInt32();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Win32.WM_HOTKEY)
            {
                Show();
            }
            return (IntPtr)0;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.IsDown)
            {
                if (e.Key == Key.Escape)
                {
                    Hide();
                }
            }
        }

        private void Window_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(bool)e.NewValue)
            {
                // Switch back to the search tab and clear the search box
                textBox.Text = string.Empty;
                tabControl.SelectedIndex = 0;
            }
            else
            {
                /* Window is becoming visible - we want to put it somewhere
                 * where the user will immediately see it, ideally right where
                 * they were typing. */
                Win32.GUITHREADINFO gui = new Win32.GUITHREADINFO();
                gui.cbSize = Marshal.SizeOf(gui);
                bool success = Win32.GetGUIThreadInfo(0, ref gui);
                if (success)
                {
                    if (gui.hwndCaret == IntPtr.Zero)
                    {
                        /* The focused application has no caret information, so
                         * gracefully degrade by using the cursor position. */
                        int left = System.Windows.Forms.Cursor.Position.X;
                        int top = System.Windows.Forms.Cursor.Position.Y;
                        PutWindowNear(left, top, top);
                    }
                    else
                    {
                        /* The GUI's caret position is relative to its control,
                         * so get the control's position and add the two. */
                        Win32.RECT windowRect = new Win32.RECT();
                        bool success2 = Win32.GetWindowRect(gui.hwndCaret, ref windowRect);
                        if (success2)
                        {
                            int left = windowRect.left + gui.rcCaret.left;
                            int top = windowRect.top + gui.rcCaret.top;
                            int bottom = windowRect.top + gui.rcCaret.bottom;
                            PutWindowNear(left, top, bottom);
                        }
                        else
                        {
                            /* TODO: before production release, handle these
                             * exceptions and always fall back to cursor pos */
                            throw new Win32Exception();
                        }
                    }
                    
                }
                else
                {
                    // TODO: see above
                    throw new Win32Exception();
                }

                // Focus textbox once it's re-marked as visible
                DependencyPropertyChangedEventHandler handler = null;
                handler = delegate
                {
                    textBox.Focus();
                    textBox.IsVisibleChanged -= handler;
                };
                textBox.IsVisibleChanged += handler;
            }
        }

        private void PutWindowNear(int left, int top, int bottom)
        {
            IntPtr monitor = Win32.MonitorFromPoint(new Win32.POINT(left, (top + bottom) / 2), Win32.MonitorOptions.MONITOR_DEFAULTTONEAREST);
            Win32.MONITORINFO monitorInfo = new Win32.MONITORINFO();
            monitorInfo.cbSize = Marshal.SizeOf(monitorInfo);
            Win32.GetMonitorInfo(monitor, ref monitorInfo);
            Win32.RECT workArea = monitorInfo.rcWork;

            /* Wherever the window spawns, put it just below and to the left
             * of the focal point, for aesthetic reasons. */
            int leftOffset = -5;
            int topOffset = -5;
            int bottomOffset = 5;

            int newRight = left + (int)ActualWidth + leftOffset;
            int newBottom = bottom + (int)ActualHeight + bottomOffset;
            if (newRight > workArea.right)
            {
                left -= (int)ActualWidth;
            }
            if (newBottom > workArea.bottom)
            {
                top -= (int)ActualHeight;
                top += topOffset;
            }
            else
            {
                top = bottom;
                top += bottomOffset;
            }

            Left = Math.Max(left + leftOffset, 0);
            Top = Math.Max(top, 0);
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            Hide();
            e.Cancel = true;
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchResults.ItemsSource = results = unicodexSearch.Search(new Model.Query(textBox.Text));
            UpdateSelectedResult(0);

        }

        private void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (results != null && results.Count > 0)
            {
                if (e.IsDown)
                {
                    // Use up/down arrow keys to navigate search results
                    if (e.Key == Key.Down)
                    {
                        UpdateSelectedResult(SearchResults.SelectedIndex + 1);
                    }
                    else if (e.Key == Key.Up)
                    {
                        UpdateSelectedResult(SearchResults.SelectedIndex - 1);
                    }
                    // Use Enter to send the selected character
                    else if (e.Key == Key.Enter)
                    {
                        SendCharacter(results[SearchResults.SelectedIndex]);
                    }
                }
            }
        }

        private void SendCharacter(View.Character c)
        {
            // Build key data for SendInput
            // FIXME: astral plane characters break in Notepad++
            Win32.INPUT[] inputs = new Win32.INPUT[c.Value.Length];
            int iChr = 0;
            foreach (char chr in c.Value)
            {
                inputs[iChr] = new Win32.INPUT();
                inputs[iChr].type = Win32.InputType.KEYBOARD;
                inputs[iChr].U.ki = new Win32.KEYBDINPUT();
                inputs[iChr].U.ki.wVk = 0;
                inputs[iChr].U.ki.wScan = (short)chr;
                inputs[iChr].U.ki.dwFlags = Win32.KEYEVENTF.UNICODE;
                inputs[iChr].U.ki.time = 0;
                iChr++;
            }

            // Send keys as soon as Unicodex is done hiding itself
            EventHandler handler = null;
            handler = delegate(object sender, EventArgs e)
            {
                uint result = Win32.SendInput((uint)c.Value.Length, inputs, Marshal.SizeOf(inputs[0]));
                if (result == 0)
                {
                    throw new Win32Exception();
                }
                Deactivated -= handler;
            };
            Deactivated += handler;
            Hide();
        }

        private void UpdateSelectedResult(int selected)
        {
            if (results != null && results.Count > 0)
            {
                SearchResults.SelectedIndex = Math.Min(Math.Max(selected, 0), results.Count - 1);
                SearchResults.ScrollIntoView(SearchResults.SelectedItem);
            }

        }

        private void navButton_Click(object sender, RoutedEventArgs e)
        {
            navButton.ContextMenu.IsEnabled = true;
            navButton.ContextMenu.PlacementTarget = (sender as Button);
            navButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Left;
            navButton.ContextMenu.IsOpen = true;
            navButton.ContextMenu.HorizontalOffset = navButton.ActualWidth + 5;
            navButton.ContextMenu.VerticalOffset = navButton.ActualHeight;
        }

        private void Shutdown()
        {
            // Unregister gloabl hotkey (probably not necessary, but as a formality...)
            Win32.UnregisterHotKey((IntPtr)new WindowInteropHelper(Application.Current.MainWindow).Handle.ToInt32(), 0);
            
            // Remove tray icon ASAP (otherwise will only disappear when user hovers over it)
            notifyIcon.Icon = null;

            Application.Current.Shutdown();
        }
    }
}
