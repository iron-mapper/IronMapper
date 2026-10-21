using IronMapper.AotProbe.Transitive.Maps;
using IronMapper.AotProbe.Transitive.Services;

// NativeAOT probe C: IMapper through a library that references IronMapper.Extensions.DI, mappings in a library
// without it. eng/verify-aot.ps1 requires zero trim/AOT warnings and exit code 0 (JIT and NativeAOT).
var mapper = MapperFactory.Create(typeof(Customer).Assembly);
var failures = 0;

void Check(bool condition, string what)
{
    if (condition) return;
    Console.WriteLine("FAILED: " + what);
    failures++;
}

var customer = new Customer { Id = 7, Name = "Ann", Addresses = { new Address { City = "Oslo" } } };

var dto = mapper.Map<Customer, CustomerDto>(customer);
Check(dto.Id == 7 && dto.Name == "Ann", "Map<Customer, CustomerDto>");
Check(dto.Addresses.Count == 1 && dto.Addresses[0].Town == "Oslo", "nested collection with a profile mapping");
Check(mapper.Map<CustomerDto>(customer).Id == 7, "Map<CustomerDto>(object)");
Check(mapper.Map<List<AddressDto>>(customer.Addresses)[0].Town == "Oslo", "Map<List<AddressDto>>(list)");

Console.WriteLine(failures == 0 ? "Transitive IMapper probe: OK" : $"Transitive IMapper probe: {failures} check(s) failed");
return failures == 0 ? 0 : 1;
