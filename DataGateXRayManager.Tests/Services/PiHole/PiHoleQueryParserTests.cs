using DataGateXRayManager.Services.PiHole;

namespace DataGateXRayManager.Tests.Services.PiHole;

public class PiHoleQueryParserTests
{
    [Fact]
    public void ParseQueriesResponse_ReadsV6Shape()
    {
        const string json = """
            {
              "queries": [
                {
                  "id": 42,
                  "time": 1719043200,
                  "domain": "example.com",
                  "client": { "ip": "10.80.0.5", "name": "laptop" },
                  "type": "A",
                  "status": "FORWARDED"
                }
              ]
            }
            """;

        var records = PiHoleQueryParser.ParseQueriesResponse(json);

        Assert.Single(records);
        Assert.Equal(42, records[0].PiHoleQueryId);
        Assert.Equal("10.80.0.5", records[0].ClientIp);
        Assert.Equal("example.com", records[0].Domain);
        Assert.Equal("A", records[0].QueryType);
        Assert.Equal("FORWARDED", records[0].Status);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1719043200), records[0].QueriedAtUtc);
    }

    [Fact]
    public void ParseQueriesResponse_ReadsStringClient()
    {
        const string json = """
            {
              "queries": [
                {
                  "id": 7,
                  "timestamp": 1719043300,
                  "domain": "tracker.example",
                  "client": "10.80.0.9",
                  "status": "GRAVITY"
                }
              ]
            }
            """;

        var records = PiHoleQueryParser.ParseQueriesResponse(json);

        Assert.Single(records);
        Assert.Equal("10.80.0.9", records[0].ClientIp);
        Assert.Equal("GRAVITY", records[0].Status);
    }

    [Fact]
    public void ParseQueriesResponse_SkipsRowsWithoutIdDomainOrClient()
    {
        const string json = """
            {
              "queries": [
                { "id": 0, "domain": "a.example", "client": "10.0.0.1" },
                { "id": 1, "domain": "", "client": "10.0.0.1" },
                { "id": 2, "domain": "b.example", "client": "" },
                { "id": 3, "domain": "ok.example", "client": "10.0.0.2", "status": "FORWARDED", "time": 100 }
              ]
            }
            """;

        var records = PiHoleQueryParser.ParseQueriesResponse(json);
        Assert.Single(records);
        Assert.Equal(3, records[0].PiHoleQueryId);
    }

    [Fact]
    public void ParseQueriesResponse_EmptyOrInvalid_ReturnsEmpty()
    {
        Assert.Empty(PiHoleQueryParser.ParseQueriesResponse(""));
        Assert.Empty(PiHoleQueryParser.ParseQueriesResponse("""{"queries":{}}"""));
    }

    [Fact]
    public void ReadSessionId_ParsesNestedSession()
    {
        const string json = """{"session":{"sid":"abc+123=","validity":1800}}""";
        Assert.Equal("abc+123=", PiHoleQueryParser.ReadSessionId(json));
    }

    [Fact]
    public void ReadRecordsTotal_ParsesPaginationField()
    {
        Assert.Equal(1234, PiHoleQueryParser.ReadRecordsTotal("""{"queries":[],"recordsTotal":1234}"""));
        Assert.Equal(99, PiHoleQueryParser.ReadRecordsTotal("""{"recordsTotal":"99"}"""));
        Assert.Null(PiHoleQueryParser.ReadRecordsTotal("""{"queries":[]}"""));
    }
}
