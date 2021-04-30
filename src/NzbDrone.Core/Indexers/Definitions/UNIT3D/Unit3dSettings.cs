using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Indexers.Definitions.UNIT3D
{
    public class Unit3dSettingsValidator : AbstractValidator<Unit3dSettings>
    {
        public Unit3dSettingsValidator()
        {
            RuleFor(c => c.ApiKey).NotEmpty();
        }
    }

    public class Unit3dSettings : IProviderConfig
    {
        private static readonly Unit3dSettingsValidator Validator = new Unit3dSettingsValidator();

        public Unit3dSettings()
        {
        }

        [FieldDefinition(1, Label = "Api Key", HelpText = "Api key generated in My Security", Privacy = PrivacyLevel.ApiKey)]
        public string ApiKey { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}