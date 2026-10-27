using Microsoft.AspNetCore.Mvc;

using OrderManagement.Application.DTOs;

using OrderManagement.Application.Interfaces;

namespace OrderManagement.API.Controllers
{
    [ApiController]
    [Route("api/products")]
    public class ProductsController : ControllerBase
    {
        private readonly IProductService _productService;
        public ProductsController(IProductService productService)
        {
            _productService = productService;
        }
        
        [HttpGet]
        public async Task<IActionResult> GetProductsAsync(CancellationToken cancellationToken)
        {
            var products = await _productService.GetProductDtosAsync(cancellationToken);
            return Ok(products);
        }

        [HttpGet("{id:int}", Name = "GetProductById")]
        public async Task<IActionResult> GetProductById(int id, CancellationToken cancellationToken)
        {
            var product = await _productService.GetProductByIdAsync(id, cancellationToken);
            if (product == null)
            {
                return NotFound();
            }
            return Ok(product);
        }

        [HttpPost]
        public async Task<ActionResult<ProductDto>> CreateProductAsync([FromBody] CreateProductDto dto, CancellationToken cancellationToken)
        {
            var product = await _productService.CreateProductAsync(dto, cancellationToken);
            return CreatedAtRoute("GetProductById", new { id = product.Id }, product);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateProductAsync(int id, [FromBody] UpdateProductDto dto, CancellationToken cancellationToken)
        {
            var product = await _productService.UpdateProductAsync(id, dto, cancellationToken);
            if (product == null)
            {
                return NotFound();
            }
            return Ok(product);
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteProductAsync(int id, CancellationToken cancellationToken)
        {
            var deleted = await _productService.DeleteProductAsync(id, cancellationToken);
            if (!deleted)
            {
                return NotFound();
            }
            return NoContent();
        }   

    }
}
