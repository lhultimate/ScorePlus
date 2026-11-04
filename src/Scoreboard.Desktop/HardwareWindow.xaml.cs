using System.Globalization;
using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;

namespace Scoreboard.Desktop;

public partial class HardwareWindow : Window
{
    readonly UserPreferences _preferences;
    readonly HardwareService _service;
    readonly HardwareSettings _draft;
    bool _loading = true;

    public HardwareWindow(UserPreferences preferences, HardwareService service)
    {
        InitializeComponent();
        _preferences = preferences;
        _service = service;
        _draft = preferences.Hardware.Clone();
        DeviceSelector.ItemsSource = HardwareDevices.All;
        TransportSelector.ItemsSource = HardwareTransports.All;
        RefreshPorts();
        LoadFields();
        _service.Status += ServiceStatus;
        _service.Raw += ServiceRaw;
        Closed += (_, _) =>
        {
            _service.Status -= ServiceStatus;
            _service.Raw -= ServiceRaw;
        };
        StatusText.Text = _service.IsRunning ? "Connected. ScorePlus is following the controller." : "Not connected.";
    }

    void LoadFields()
    {
        _loading = true;
        try
        {
            DeviceSelector.SelectedItem = HardwareDevices.Find(_draft.DeviceId);
            TransportSelector.SelectedItem = HardwareTransports.All.FirstOrDefault(option =>
                string.Equals(option.Value, _draft.Transport, StringComparison.OrdinalIgnoreCase))
                ?? HardwareTransports.All[0];
            AutoConnectInput.IsChecked = _draft.AutoConnect;
            BaudInput.Text = _draft.BaudRate.ToString(CultureInfo.InvariantCulture);
            MqttHostInput.Text = _draft.MqttHost;
            MqttPortInput.Text = _draft.MqttPort.ToString(CultureInfo.InvariantCulture);
            MqttTlsInput.IsChecked = _draft.MqttTls;
            MqttTopicInput.Text = _draft.MqttTopic;
            MqttUserInput.Text = _draft.MqttUsername;
            MqttPasswordInput.Password = _draft.MqttPassword;
            WebSocketUrlInput.Text = _draft.WebSocketUrl;
            WebSocketTokenInput.Password = _draft.WebSocketToken;
            if (!string.IsNullOrWhiteSpace(_draft.SerialPort) && SerialPortSelector.Items.Contains(_draft.SerialPort))
                SerialPortSelector.SelectedItem = _draft.SerialPort;
            else if (SerialPortSelector.Items.Count > 0)
                SerialPortSelector.SelectedIndex = 0;
            UpdateNotes();
            UpdateVisibility();
        }
        finally { _loading = false; }
    }

    void UpdateNotes()
    {
        if (DeviceSelector.SelectedItem is not HardwareDeviceProfile profile) return;
        NotesText.Text = profile.Summary + "\n" + profile.Notes;
    }

    void UpdateVisibility()
    {
        var source = TransportSelector.SelectedItem is HardwareTransportOption option
            ? option.Value
            : nameof(HardwareTransport.DaktronicsRtd);
        SerialPanel.Visibility = source is nameof(HardwareTransport.DaktronicsRtd) or nameof(HardwareTransport.SerialCapture)
            ? Visibility.Visible : Visibility.Collapsed;
        MqttPanel.Visibility = source == nameof(HardwareTransport.SportzcastMqtt)
            ? Visibility.Visible : Visibility.Collapsed;
        WebSocketPanel.Visibility = source == nameof(HardwareTransport.ScorebirdWebSocket)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    void RefreshPorts()
    {
        var current = _draft.SerialPort;
        if (SerialPortSelector.SelectedItem is string selected) current = selected;
        string[] ports;
        try { ports = SerialPort.GetPortNames().OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray(); }
        catch { ports = []; }
        SerialPortSelector.ItemsSource = ports;
        if (!string.IsNullOrWhiteSpace(current) && ports.Contains(current, StringComparer.OrdinalIgnoreCase))
            SerialPortSelector.SelectedItem = ports.First(port => string.Equals(port, current, StringComparison.OrdinalIgnoreCase));
        else if (ports.Length > 0) SerialPortSelector.SelectedIndex = 0;
    }

    void Device_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || DeviceSelector.SelectedItem is not HardwareDeviceProfile profile) return;
        _draft.ApplyProfile(profile);
        LoadFields();
    }

    void Transport_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || TransportSelector.SelectedItem is not HardwareTransportOption option) return;
        _draft.Transport = option.Value;
        UpdateVisibility();
    }

    void RefreshPorts_Click(object sender, RoutedEventArgs e) => RefreshPorts();

    void Commit()
    {
        if (DeviceSelector.SelectedItem is HardwareDeviceProfile profile) _draft.DeviceId = profile.Id;
        if (TransportSelector.SelectedItem is HardwareTransportOption option) _draft.Transport = option.Value;
        _draft.SerialPort = SerialPortSelector.Text.Trim();
        if (int.TryParse(BaudInput.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var baud))
            _draft.BaudRate = Math.Clamp(baud, 300, 921600);
        _draft.AutoConnect = AutoConnectInput.IsChecked == true;
        _draft.MqttHost = MqttHostInput.Text.Trim();
        if (int.TryParse(MqttPortInput.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port))
            _draft.MqttPort = Math.Clamp(port, 1, 65535);
        _draft.MqttTls = MqttTlsInput.IsChecked == true;
        _draft.MqttTopic = MqttTopicInput.Text.Trim();
        _draft.MqttUsername = MqttUserInput.Text.Trim();
        _draft.MqttPassword = MqttPasswordInput.Password;
        _draft.WebSocketUrl = WebSocketUrlInput.Text.Trim();
        _draft.WebSocketToken = WebSocketTokenInput.Password;
        _preferences.Hardware = _draft.Clone();
        PreferencesStore.Save(_preferences);
    }

    void Connect_Click(object sender, RoutedEventArgs e)
    {
        Commit();
        _service.Start(_preferences.Hardware);
        StatusText.Text = "Connecting…";
    }

    void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        _service.Stop();
        StatusText.Text = "Disconnected.";
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        Commit();
        StatusText.Text = "Hardware settings saved.";
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void ServiceStatus(string message)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => StatusText.Text = message)); return; }
        StatusText.Text = message;
    }

    void ServiceRaw(string message)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => ServiceRaw(message))); return; }
        LogText.AppendText(message + Environment.NewLine);
        if (LogText.Text.Length > 24000) LogText.Text = LogText.Text[^20000..];
        LogText.ScrollToEnd();
    }
}
