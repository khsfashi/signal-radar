namespace SignalRadar.Application.Articles;

public interface IUrlCanonicalizer
{
    public Uri Normalize(string url);
}
