using System.ComponentModel;
using PowerMonitor.Models;
using PowerMonitor.Services;

namespace PowerMonitor.Views;

/// <summary>
/// 主界面：展示当前电源状态、系统电源事件日志，并提供托盘通知。
/// </summary>
public sealed class MainForm : Form
{
    private const int MaxLogEntries = 2000;

    private readonly PowerStateService _service;
    private readonly SystemPowerService _powerService = new();
    private readonly BatteryQueryService _batteryQuery = new();
    private readonly AutoStartService _autoStart = new();
    private ToolStripMenuItem? _autoStartItem;
    private readonly ListView _logView;
    private readonly Label _lblPowerSourceValue = new();
    private readonly Label _lblBatteryStateValue = new();
    private readonly Label _lblPercentValue = new();
    private readonly Label _lblRemainingValue = new();
    private readonly Label _lblSaverValue = new();
    private readonly Label _lblDisplayValue = new() { Text = "等待事件…" };
    private readonly Label _lblLidValue = new() { Text = "等待事件（台式机通常无合盖设备）" };
    private readonly Label _lblAdapterValue = new() { Text = "等待事件…" };
    private readonly Label _lblBatteryDetailValue = new() { Text = "点击\"检测电池\"查询" };
    private readonly Label _lblUpdatedValue = new();
    private readonly NotifyIcon _trayIcon;
    private readonly IContainer _components = new Container();

    private bool _realExit;

    public MainForm(PowerStateService service)
    {
        _service = service;
        HandleDestroyed += (_, _) => AppLog.Log($"主窗体句柄销毁 (IsHandleCreated={IsHandleCreated})");

        Text = "电源状态监控";
        Width = 760;
        Height = 680;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(600, 540);

        var statusGroup = BuildStatusGroup();
        var bottomPanel = BuildBottomPanel();

        _logView = BuildLogView();
        var logGroup = new GroupBox
        {
            Text = "事件日志（系统电源事件实时推送，无轮询）",
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };
        logGroup.Controls.Add(_logView);

        // 注意停靠顺序：先加入 Fill，再加入 Top/Bottom，保证边缘面板优先停靠。
        Controls.Add(logGroup);
        Controls.Add(statusGroup);
        Controls.Add(bottomPanel);

        // 菜单栏最后加入，使其停靠在最顶端（WinForms 按加入顺序逆序停靠）。
        var menuStrip = BuildMenuStrip();
        Controls.Add(menuStrip);
        MainMenuStrip = menuStrip;

        _trayIcon = BuildTrayIcon();

        Load += OnLoad;
        FormClosing += OnFormClosing;
        _service.PowerModeChanged += OnPowerModeChanged;
    }

    private static ListView BuildLogView()
    {
        var view = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            GridLines = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            Font = new Font("Consolas", 9F)
        };
        view.Columns.Add("时间", 170, HorizontalAlignment.Left);
        view.Columns.Add("事件", 170, HorizontalAlignment.Left);
        view.Columns.Add("详情", 320, HorizontalAlignment.Left);
        return view;
    }

    private GroupBox BuildStatusGroup()
    {
        var group = new GroupBox
        {
            Text = "当前电源状态",
            Dock = DockStyle.Top,
            Height = 244,
            Padding = new Padding(8)
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 10
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        AddStatusRow(table, 0, "供电方式：", _lblPowerSourceValue);
        AddStatusRow(table, 1, "适配器状态：", _lblAdapterValue);
        AddStatusRow(table, 2, "电池状态：", _lblBatteryStateValue);
        AddStatusRow(table, 3, "剩余电量：", _lblPercentValue);
        AddStatusRow(table, 4, "预计剩余时间：", _lblRemainingValue);
        AddStatusRow(table, 5, "节电模式：", _lblSaverValue);
        AddStatusRow(table, 6, "显示器状态：", _lblDisplayValue);
        AddStatusRow(table, 7, "合盖状态：", _lblLidValue);
        AddStatusRow(table, 8, "电池详情：", _lblBatteryDetailValue);
        AddStatusRow(table, 9, "状态更新时间：", _lblUpdatedValue);

        group.Controls.Add(table);
        return group;
    }

    private static void AddStatusRow(TableLayoutPanel table, int row, string caption, Label value)
    {
        var captionLabel = new Label
        {
            Text = caption,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.DimGray
        };
        value.Dock = DockStyle.Fill;
        value.TextAlign = ContentAlignment.MiddleLeft;
        value.Font = new Font(value.Font, FontStyle.Bold);

        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / 10F));
        table.Controls.Add(captionLabel, 0, row);
        table.Controls.Add(value, 1, row);
    }

    private Panel BuildBottomPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(8, 8, 8, 8)
        };

        var hint = new Label
        {
            Text = "提示：关闭窗口将最小化到系统托盘，可在托盘图标右键退出。",
            Dock = DockStyle.Left,
            AutoSize = true,
            ForeColor = Color.Gray,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var btnClear = new Button { Text = "清空日志", Width = 90, Dock = DockStyle.Right };
        btnClear.Click += (_, _) => _logView.Items.Clear();

        var btnRefresh = new Button { Text = "刷新状态", Width = 90, Dock = DockStyle.Right, Margin = new Padding(0, 0, 8, 0) };
        btnRefresh.Click += (_, _) => UpdateStatus(_service.GetCurrentStatus());

        var btnDetectBattery = new Button { Text = "检测电池", Width = 90, Dock = DockStyle.Right, Margin = new Padding(0, 0, 8, 0) };
        btnDetectBattery.Click += (_, _) => DetectBattery();

        panel.Controls.Add(hint);
        panel.Controls.Add(btnDetectBattery);
        panel.Controls.Add(btnRefresh);
        panel.Controls.Add(btnClear);
        return panel;
    }

    private MenuStrip BuildMenuStrip()
    {
        var strip = new MenuStrip();

        var powerMenu = new ToolStripMenuItem("电源操作(&P)");
        powerMenu.DropDownItems.Add("睡眠(&S)", null, (_, _) => InvokePowerAction(PowerAction.Sleep));
        powerMenu.DropDownItems.Add("休眠(&H)", null, (_, _) => InvokePowerAction(PowerAction.Hibernate));
        powerMenu.DropDownItems.Add(new ToolStripSeparator());
        powerMenu.DropDownItems.Add("关机(&U)…", null, (_, _) => InvokePowerAction(PowerAction.Shutdown));
        powerMenu.DropDownItems.Add("重启(&R)…", null, (_, _) => InvokePowerAction(PowerAction.Restart));
        strip.Items.Add(powerMenu);

        // 设置菜单：开机自启开关
        var settingsMenu = new ToolStripMenuItem("设置(&S)");
        _autoStartItem = new ToolStripMenuItem("开机自启动(&A)")
        {
            CheckOnClick = false,
            Checked = _autoStart.IsEnabled()
        };
        _autoStartItem.Click += (_, _) => ToggleAutoStart();
        settingsMenu.DropDownItems.Add(_autoStartItem);
        strip.Items.Add(settingsMenu);

        return strip;
    }

    private NotifyIcon BuildTrayIcon()
    {
        var menu = new ContextMenuStrip(_components);
        menu.Items.Add("睡眠", null, (_, _) => InvokePowerAction(PowerAction.Sleep));
        menu.Items.Add("休眠", null, (_, _) => InvokePowerAction(PowerAction.Hibernate));
        menu.Items.Add("-");
        menu.Items.Add("关机…", null, (_, _) => InvokePowerAction(PowerAction.Shutdown));
        menu.Items.Add("重启…", null, (_, _) => InvokePowerAction(PowerAction.Restart));
        menu.Items.Add("-");
        menu.Items.Add("显示主界面", null, (_, _) => ShowFromTray());
        menu.Items.Add("退出", null, (_, _) => ExitApplication());

        var icon = new NotifyIcon(_components)
        {
            Icon = SystemIcons.Application,
            Text = "电源状态监控",
            Visible = true,
            ContextMenuStrip = menu
        };
        icon.DoubleClick += (_, _) => ShowFromTray();
        return icon;
    }

    private void OnLoad(object? sender, EventArgs e)
    {
        UpdateStatus(_service.GetCurrentStatus());
        AppendLog(DateTimeOffset.Now, "监控已启动",
            "已订阅 WM_POWERBROADCAST 及显示器开关、合盖动作、适配器插拔通知");
        var autoStartOn = _autoStart.IsEnabled();
        AppLog.Log($"开机自启状态：{(autoStartOn ? "已启用" : "未启用")}");
        AppendLog(DateTimeOffset.Now, "开机自启", autoStartOn ? "已启用" : "未启用");
        DetectBattery();
    }

    private void OnPowerModeChanged(object? sender, PowerStateChangedEventArgs e)
    {
        // 回调虽来自 UI 线程的窗口过程，仍经 BeginInvoke 入队，避免在系统广播调用栈内直接重入更新 UI。
        BeginInvoke(() =>
        {
            try
            {
                string detail = e.Kind switch
                {
                    PowerEventKind.DisplayStateChanged
                    or PowerEventKind.LidSwitchStateChanged
                    or PowerEventKind.AdapterConnected
                        => PowerStateService.DescribeSetting(e.Kind, e.SettingValue ?? -1),
                    PowerEventKind.Unknown
                        => $"未识别的广播代码 0x{e.RawCode.ToInt64():X4}",
                    _ => e.Status?.ToSummary() ?? "无法获取电源状态"
                };

                AppendLog(e.Timestamp, e.KindText, detail);
                UpdateStatus(e.Status);
                UpdateSwitchState(e.Kind, e.SettingValue, detail);
                ShowBalloon(e.Kind, e.SettingValue, e.KindText, detail);

                // 适配器拔出时自动检测电池详情
                if (e.Kind == PowerEventKind.AdapterConnected && e.SettingValue == 1)
                {
                    AppendLog(DateTimeOffset.Now, "适配器拔出", "已切换至电池供电，正在检测系统电池…");
                    DetectBattery();
                }
            }
            catch (Exception ex)
            {
                AppLog.Log($"处理电源事件 {e.Kind} 时发生异常", ex);
            }
        });
    }

    private void UpdateStatus(PowerStatusInfo? status)
    {
        if (status is null)
        {
            _lblPowerSourceValue.Text = "获取失败";
            return;
        }

        _lblPowerSourceValue.Text = status.PowerSourceText;
        _lblBatteryStateValue.Text = status.BatteryStateText;
        _lblPercentValue.Text = status.PercentText;
        _lblRemainingValue.Text = status.RemainingTimeText;
        _lblSaverValue.Text = status.HasBattery ? (status.BatterySaverOn ? "已开启" : "未开启") : "—";
        _lblUpdatedValue.Text = status.CapturedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

        _lblPowerSourceValue.ForeColor = status.IsBatteryPower ? Color.DarkOrange : Color.SeaGreen;
        _trayIcon.Text = TruncateForTray($"电源监控 - {status.ToSummary()}");
    }

    /// <summary>根据显示器/合盖/适配器事件刷新对应状态行的文本与颜色。</summary>
    private void UpdateSwitchState(PowerEventKind kind, int? value, string text)
    {
        switch (kind)
        {
            case PowerEventKind.DisplayStateChanged when value is int display:
                _lblDisplayValue.Text = text;
                _lblDisplayValue.ForeColor = display == 0 ? Color.DarkOrange : Color.SeaGreen;
                break;
            case PowerEventKind.LidSwitchStateChanged when value is int lid:
                _lblLidValue.Text = text;
                _lblLidValue.ForeColor = lid == 0 ? Color.DarkOrange : Color.SeaGreen;
                break;
            case PowerEventKind.AdapterConnected when value is int ac:
                _lblAdapterValue.Text = text;
                _lblAdapterValue.ForeColor = ac == 0 ? Color.SeaGreen : Color.DarkOrange;
                break;
        }
    }

    /// <summary>
    /// 通过 WMI 检测系统自带的电池，查询设计容量、满充容量、循环次数、健康度等。
    /// 在后台线程执行避免阻塞 UI。
    /// </summary>
    private void DetectBattery()
    {
        _lblBatteryDetailValue.Text = "正在检测…";
        _lblBatteryDetailValue.ForeColor = Color.Gray;

        Task.Run(() =>
        {
            try
            {
                var batteries = _batteryQuery.QueryAllBatteries();
                BeginInvoke(() =>
                {
                    if (batteries.Count == 0)
                    {
                        _lblBatteryDetailValue.Text = "未检测到电池设备";
                        _lblBatteryDetailValue.ForeColor = Color.DarkOrange;
                        AppendLog(DateTimeOffset.Now, "电池检测", "未检测到系统自带电池");
                    }
                    else
                    {
                        var summary = string.Join(" | ", batteries.Select(b => b.ToSummary()));
                        _lblBatteryDetailValue.Text = summary;
                        var anyHealthy = batteries.Any(b => b.HealthPercent >= 60);
                        _lblBatteryDetailValue.ForeColor = anyHealthy ? Color.SeaGreen : Color.DarkOrange;
                        foreach (var b in batteries)
                        {
                            AppendLog(DateTimeOffset.Now, "电池检测",
                                $"{b.InstanceName}：{b.ToSummary()}");
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Log("电池检测失败", ex);
                BeginInvoke(() =>
                {
                    _lblBatteryDetailValue.Text = "检测失败";
                    _lblBatteryDetailValue.ForeColor = Color.Red;
                });
            }
        });
    }

    /// <summary>切换开机自启动状态。</summary>
    private void ToggleAutoStart()
    {
        try
        {
            if (_autoStart.IsEnabled())
            {
                _autoStart.Disable();
                _autoStartItem!.Checked = false;
                AppendLog(DateTimeOffset.Now, "开机自启", "已禁用");
            }
            else
            {
                _autoStart.Enable();
                _autoStartItem!.Checked = true;
                AppendLog(DateTimeOffset.Now, "开机自启", $"已启用：{Environment.ProcessPath}");
            }
        }
        catch (Exception ex)
        {
            AppLog.Log("切换开机自启失败", ex);
            MessageBox.Show(this, ex.Message, "设置失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void AppendLog(DateTimeOffset time, string mode, string detail)
    {
        _logView.BeginUpdate();
        _logView.Items.Add(new ListViewItem(new[]
        {
            time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            mode,
            detail
        }));

        if (_logView.Items.Count > MaxLogEntries)
        {
            _logView.Items.RemoveAt(0);
        }

        _logView.Items[^1].EnsureVisible();
        _logView.EndUpdate();
    }

    private void ShowBalloon(PowerEventKind kind, int? settingValue, string title, string text)
    {
        // 睡眠、低电量、显示器关闭、合上盖子用警告气泡；其余为信息提示。
        var warning = kind is PowerEventKind.Suspend
            or PowerEventKind.BatteryLow
            or PowerEventKind.QuerySuspend
            || (kind == PowerEventKind.DisplayStateChanged && settingValue == 0)
            || (kind == PowerEventKind.LidSwitchStateChanged && settingValue == 0)
            || (kind == PowerEventKind.AdapterConnected && settingValue == 1);
        _trayIcon.ShowBalloonTip(3000, title, text, warning ? ToolTipIcon.Warning : ToolTipIcon.Info);
    }

    private enum PowerAction { Sleep, Hibernate, Shutdown, Restart }

    /// <summary>
    /// 执行系统电源操作。关机/重启需要二次确认；所有 API 异常在此统一提示。
    /// </summary>
    private void InvokePowerAction(PowerAction action)
    {
        var (name, needConfirm, warning) = action switch
        {
            PowerAction.Sleep => ("睡眠", false, false),
            PowerAction.Hibernate => ("休眠", false, false),
            PowerAction.Shutdown => ("关机", true, true),
            PowerAction.Restart => ("重启", true, false),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };

        if (needConfirm)
        {
            var confirm = MessageBox.Show(
                this,
                $"确定要立即{name}吗？系统将通知所有程序退出，未保存的工作可能丢失。",
                $"确认{name}",
                MessageBoxButtons.OKCancel,
                warning ? MessageBoxIcon.Warning : MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.OK) return;
        }

        AppendLog(DateTimeOffset.Now, $"用户手动发起{name}", $"调用系统电源 API：{name}");

        try
        {
            switch (action)
            {
                case PowerAction.Sleep:
                    _powerService.Sleep();
                    break;
                case PowerAction.Hibernate:
                    _powerService.Hibernate();
                    break;
                case PowerAction.Shutdown:
                    _powerService.Shutdown();
                    break;
                case PowerAction.Restart:
                    _powerService.Restart();
                    break;
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            AppendLog(DateTimeOffset.Now, $"{name}失败", ex.Message);
            MessageBox.Show(this, ex.Message, $"{name}失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        AppLog.Log($"收到窗体关闭请求，CloseReason={e.CloseReason}，_realExit={_realExit}");
        if (_realExit) return;
        // 点关闭按钮时隐藏到托盘，保持事件监听不中断。
        e.Cancel = true;
        Hide();
    }

    private void ExitApplication()
    {
        AppLog.Log("用户从托盘菜单退出");
        _realExit = true;
        _service.PowerModeChanged -= OnPowerModeChanged;
        _trayIcon.Visible = false;
        Application.Exit();
    }

    private static string TruncateForTray(string text) =>
        text.Length <= 63 ? text : text[..63];

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _service.PowerModeChanged -= OnPowerModeChanged;
            _components.Dispose();
        }
        base.Dispose(disposing);
    }
}
