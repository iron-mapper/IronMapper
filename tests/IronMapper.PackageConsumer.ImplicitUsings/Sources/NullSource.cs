// IronMapperNullSourceReturnsNull=true (issue #27): MapToX(this S? source) returns D? with
// [return: NotNullIfNotNull("source")], null -> null. Built with TreatWarningsAsErrors and nullable annotations: code that
// maps a non-null source and uses the result as non-null (written for the default) gets no CS8602/CS8600. Also built as a
// netstandard2.0 library, where the generator declares an internal NotNullIfNotNullAttribute.
using System;
using System.Collections.Generic;
using IronMapper.Attributes;
using IronMapper.Generated;

namespace NullSourceCheck
{
    public static class Program
    {
        public static int Main()
        {
            var failures = new List<string>();

            var order = new Order { Name = "o", Lines = new List<Line> { new Line { Sku = "a" } } };
            var dto = order.MapToOrderDto();
            OrderDto typed = order.MapToOrderDto();
            if (dto.Name != "o" || typed.Lines[0].Sku != "a") failures.Add("mapped " + dto.Name);

            Order? none = null;
            if (none.MapToOrderDto() != null) failures.Add("null source mapped to an object");
            if (none.MapToOrderDto()?.Name != null) failures.Add("null source has a name");

            try
            {
                new List<Order> { null! }.MapToOrderDtoList();
                failures.Add("null element mapped");
            }
            catch (ArgumentNullException)
            {
            }

            if (failures.Count > 0)
            {
                Console.Error.WriteLine("FAIL: " + string.Join("; ", failures));
                return 1;
            }

            Console.WriteLine("NullSource: OK");
            return 0;
        }
    }

    [MapTo(typeof(OrderDto))]
    public class Order
    {
        public string Name { get; set; } = "";
        public List<Line> Lines { get; set; } = new List<Line>();
    }

    public class OrderDto
    {
        public string Name { get; set; } = "";
        public List<LineDto> Lines { get; set; } = new List<LineDto>();
    }

    [MapTo(typeof(LineDto))]
    public class Line
    {
        public string Sku { get; set; } = "";
    }

    public class LineDto
    {
        public string Sku { get; set; } = "";
    }
}
