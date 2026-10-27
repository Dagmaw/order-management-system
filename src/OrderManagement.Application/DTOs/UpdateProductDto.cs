using System.ComponentModel.DataAnnotations;

namespace OrderManagement.Application.DTOs
{
    public class UpdateProductDto
    {

        [Required]
        [StringLength(200)]
        public string Name { get; set; } = String.Empty;

        [StringLength(200)]
        public string Description { get; set; } = String.Empty;

        [Range(typeof(decimal), "0.01", "9999999999999999.99")]
        public decimal Price { get; set; }
    }
}
