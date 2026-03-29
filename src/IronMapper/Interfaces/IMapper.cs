namespace IronMapper.Interfaces;

/// <summary>Core mapper interface. Implementations are generated at compile-time.</summary>
public interface IMapper
{
    TDestination Map<TSource, TDestination>(TSource source);
    TDestination Map<TDestination>(object source);
}
