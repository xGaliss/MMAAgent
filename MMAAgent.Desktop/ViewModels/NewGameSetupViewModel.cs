using System.Threading.Tasks;
using MMAAgent.Desktop.Services;

namespace MMAAgent.Desktop.ViewModels
{
    public sealed class NewGameSetupViewModel : ObservableObject
    {
        private readonly NewGameService _newGameService;
        private readonly CreateAgentProfileService _createAgentProfileService;

        private string _agentName = "";
        public string AgentName
        {
            get => _agentName;
            set => SetProperty(ref _agentName, value);
        }

        private string _agencyName = "";
        public string AgencyName
        {
            get => _agencyName;
            set => SetProperty(ref _agencyName, value);
        }

        private string _errorText = "";
        public string ErrorText
        {
            get => _errorText;
            set => SetProperty(ref _errorText, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public System.Func<Task>? OnCreateCompleted { get; set; }
        public System.Action? OnCancel { get; set; }

        public NewGameSetupViewModel(
            NewGameService newGameService,
            CreateAgentProfileService createAgentProfileService)
        {
            _newGameService = newGameService;
            _createAgentProfileService = createAgentProfileService;
        }

        public async Task CreateAsync()
        {
            ErrorText = "";

            if (string.IsNullOrWhiteSpace(AgentName))
            {
                ErrorText = "Introduce el nombre del agente.";
                return;
            }

            if (string.IsNullOrWhiteSpace(AgencyName))
            {
                ErrorText = "Introduce el nombre de la agencia.";
                return;
            }

            IsBusy = true;

            try
            {
                _newGameService.CreateAndLoadNewGame("MiPartida", fighterCount: 800);
                await _createAgentProfileService.CreateAsync(AgentName, AgencyName);

                if (OnCreateCompleted != null)
                    await OnCreateCompleted();
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void Cancel()
        {
            OnCancel?.Invoke();
        }
    }
}