using System.ComponentModel.DataAnnotations;

namespace OrderManagement.Application.DTOs
{
    public class CreateProductDto
    {
        [Required]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;

        [StringLength(200)]
        public string Description { get; set; } = string.Empty;

        [Range(typeof(decimal), "0.01", "9999999999999999.99")]
        public decimal Price { get; set; }
    }
}
