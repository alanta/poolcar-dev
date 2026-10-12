using System.ComponentModel.DataAnnotations;

namespace PoolCar.Web.Clients;

public class FleetApiOptions
{
    [Required]
    public Uri? BaseAddress { get; set; }
}
