﻿using Aslenos.Services;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
namespace Aslenos.Views
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class BrowsePage : ContentPage
    {
        private Bluetooth Bluetooth { get; }

        public BrowsePage()
        {
            InitializeComponent();

            Bluetooth = DependencyService.Get<Bluetooth>();
            DevicesList.ItemsSource = Bluetooth.GetDevices();
            DevicesList.Refreshing += (s, e) => ReScanDevices();

            StartSearchDevices();
        }


        private async void DevicesList_ItemSelected(object sender, SelectedItemChangedEventArgs e)
        {
            var selectedDevice = DevicesList.SelectedItem;

            if (selectedDevice == null)
            {
                return;
            }

            var answer = await DisplayAlert("Info", "Do you want to connect to this device", "YES", "NO");

            if (!answer)
            {
                return;
            }

            var result = await Bluetooth.TryConnectToDevice(selectedDevice);

            if (result)
            {
                await DisplayAlert("Connection status:", "The device is successfully connected.", "OK");
            }
            else
            {
                var repeat = await DisplayAlert("Connection status:", "Сonnection error.\nWould you like to reconnect?", "Try again", "NO");

                if (repeat)
                {
                    DevicesList_ItemSelected(null, null);
                }
            }
        }

        private void ReScanDevices()
        {
            Bluetooth.SearchDevices();
            DevicesList.IsRefreshing = false;
        }


        private async void StartSearchDevices()
        {
            if (Bluetooth.IsBluetoothOn())
            {
                Bluetooth.SearchDevices();
            }
            else
            {
                await DisplayAlert("Error", "Please, turn on your bluetooth", "OK");
            }
        }
    }
}