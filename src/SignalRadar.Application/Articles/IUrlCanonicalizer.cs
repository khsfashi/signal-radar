namespace SignalRadar.Application.Articles;

public interface IUrlCanonicalizer
{
    Uri Normalize(string url);
}
