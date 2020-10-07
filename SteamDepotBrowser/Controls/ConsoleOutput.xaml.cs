using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace SteamDepotBrowser.Controls
{
    public partial class ConsoleOutput : UserControl
    {
        public static readonly DependencyProperty OutputTextProperty = DependencyProperty.Register(
            nameof(OutputText),
            typeof(string),
            typeof(ConsoleOutput),
            new FrameworkPropertyMetadata("")
        );

        public string OutputText
        {
            get => (string) GetValue(OutputTextProperty);
            set => SetValue(OutputTextProperty, value);
        }

        public ScrollViewer ScrollContainer { get; set; }

        private TextWriter previousConsoleOutput;
        
        public ConsoleOutput()
        {
            InitializeComponent();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            previousConsoleOutput = Console.Out;
            var newOutput = new ConsoleOutputWriter(this);
            Console.SetOut(newOutput);

            Console.WriteLine("-- Log output started --");
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Console.SetOut(previousConsoleOutput);
        }
    }

    internal class ConsoleOutputWriter : TextWriter
    {
        private readonly ConsoleOutput output;
        
        public ConsoleOutputWriter(ConsoleOutput consoleOutput)
        {
            output = consoleOutput ?? throw new ArgumentNullException(nameof(consoleOutput));
        }

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            output.Dispatcher.Invoke(() =>
            {
                output.OutputText += value;
                output.ScrollContainer.ScrollToBottom();
            });
        }

        public override void Write(string value)
        {
            output.Dispatcher.Invoke(() =>
            {
                output.OutputText += value;
                output.ScrollContainer.ScrollToBottom();
            });
        }
    }
}