using FluentValidation;
using Runnatics.Models.Client.Requests.BibMapping;

namespace Runnatics.Services.Validators
{
    public class CreateBibMappingValidator : AbstractValidator<CreateBibMappingRequest>
    {
        public CreateBibMappingValidator()
        {
            RuleFor(x => x.RaceId)
                .NotEmpty().WithMessage("RaceId is required.");

            RuleFor(x => x.BibNumber)
                .NotEmpty().WithMessage("BibNumber is required.")
                .MaximumLength(20).WithMessage("BibNumber must not exceed 20 characters.");

            RuleFor(x => x.Epc)
                .NotEmpty().WithMessage("EPC is required.")
                .MaximumLength(100).WithMessage("EPC must not exceed 100 characters.")
                .Matches(@"^[0-9A-Fa-f]+$").WithMessage("EPC must be a valid hexadecimal string.");
        }
    }
}
