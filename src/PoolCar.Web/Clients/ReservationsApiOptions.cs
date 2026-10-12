using System.ComponentModel.DataAnnotations;

namespace PoolCar.Web.Clients;

public class ReservationsApiOptions
{
    [Required]
    public Uri? BaseAddress { get; set; }
}
