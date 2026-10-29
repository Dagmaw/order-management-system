using System.ComponentModel.DataAnnotations;

namespace OrderManagement.Application.DTOs
{
    public class CreateOrderDto
    {
        [Range(1, int.MaxValue)]
        public int UserId { get; set; }

        [Required]
        [MinLength(1)]
        [MaxLength(10)]
        public List<CreateOrderItemDto> Items { get; set; } = new();
    }
}
