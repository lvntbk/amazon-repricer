namespace AmazonRepricer.Api.Auth;

public interface ILoginTimingProtector
{
    void VerifyDummyPassword(
        string providedPassword);
}
