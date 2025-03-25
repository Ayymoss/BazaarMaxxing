using AutoMapper;
using BazaarCompanionWeb.Entities;

namespace BazaarCompanionWeb.Utilities;

public class ProductProfile : Profile
{
    public ProductProfile()
    {
        CreateMap<EFProduct, EFProduct>()
            .ForMember(x => x.Snapshots, opt => opt.Ignore());
    }
}
