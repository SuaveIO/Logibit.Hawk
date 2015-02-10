﻿module logibit.hawk.Tests.Uri

open System
open Fuchu
open NodaTime

open logibit.hawk
open logibit.hawk.Encoding
open logibit.hawk.Types
open logibit.hawk.Bewit

open logibit.hawk.Tests.Shared

let ts i = Instant.FromTicksSinceUnixEpoch(i * NodaConstants.TicksPerMillisecond)

let clock =
  SystemClock.Instance

let creds_inner =
  { id        = "123456"
    key       = "2983d45yun89q"
    algorithm = SHA256 }

[<Tests>]
let ``bewit generation`` =
  let seconds i = Duration.FromSeconds i

  testList "Bewit.generate" [
    testCase "it returns a valid bewit value" <| fun _ ->
      let b =
        Bewit.generate_str_base64
          "https://example.com/somewhere/over/the/rainbow"
          { BewitOptions.credentials = creds_inner
            ttl                      = Duration.FromSeconds 300L
            clock                    = clock
            local_clock_offset       = ts 1356420407232L - clock.Now
            ext                      = Some "xandyandz" }
      Assert.Equal("bewit should generate correctly",
                   "MTIzNDU2XDEzNTY0MjA3MDdca3NjeHdOUjJ0SnBQMVQxekRMTlBiQjVVaUtJVTl0T1NKWFRVZEc3WDloOD1ceGFuZHlhbmR6",
                   b)

    testCase "returns a valid bewit value (explicit port)" <| fun _ ->
      let b =
        Bewit.generate_str_base64
          "https://example.com:8080/somewhere/over/the/rainbow"
          { BewitOptions.credentials = creds_inner
            ttl                      = Duration.FromSeconds 300L
            clock                    = clock
            local_clock_offset       = ts 1356420407232L - clock.Now
            ext                      = Some "xandyandz" }
      Assert.Equal("bewit should generate correctly",
                   "MTIzNDU2XDEzNTY0MjA3MDdcaFpiSjNQMmNLRW80a3kwQzhqa1pBa1J5Q1p1ZWc0V1NOYnhWN3ZxM3hIVT1ceGFuZHlhbmR6",
                   b)

    testCase "returns a valid bewit value (None ext)" <| fun _ ->
      let b =
        Bewit.generate_str_base64
          "https://example.com/somewhere/over/the/rainbow"
          { BewitOptions.credentials = creds_inner
            ttl                      = Duration.FromSeconds 300L
            clock                    = clock
            local_clock_offset       = ts 1356420407232L - clock.Now
            ext                      = None }
      Assert.Equal("bewit should generate correctly",
                   "MTIzNDU2XDEzNTY0MjA3MDdcSUdZbUxnSXFMckNlOEN4dktQczRKbFdJQStValdKSm91d2dBUmlWaENBZz1c",
                   b)
  ]

[<Tests>]
let ``encoding tests`` =
  testCase "it should encode and decode a uri to match the original" <| fun _ ->
    let test_uri = "http://example.com:80/resource/4?a=1&b=2"
    Assert.Equal("return value", test_uri, (ModifiedBase64Url.encode >> ModifiedBase64Url.decode) test_uri)

[<Tests>]
let ``parsing bewit parts`` =
  testCase "can parse bewit from bewit token" <| fun _ ->
    let b = Bewit.generate_str "https://example.com/somewhere/over/the/rainbow"
                               { BewitOptions.credentials = creds_inner
                                 ttl                      = Duration.FromSeconds 300L
                                 clock                    = clock
                                 local_clock_offset       = ts 1356420407232L - clock.Now
                                 ext                      = None }
    match Bewit.parse b with
    | Choice1Of2 map ->
      Assert.Equal("has id", creds_inner.id, map |> Map.find "id")
      Assert.NotEqual("has exp", "", map |> Map.find "exp")
      Assert.NotEqual("has mac", "", map |> Map.find "mac")
      Assert.Equal("has not got ext", "", map |> Map.find "ext")
    | err ->
      Tests.failtest "should have been able to parse the four token components"

let settings =
  { BewitSettings.clock = clock
    logger              = Logging.NoopLogger
    allowed_clock_skew  = Duration.FromMilliseconds 8000L
    local_clock_offset  = Duration.Zero
    creds_repo          = fun id -> Choice1Of2 (creds_inner, "steve") }

[<Tests>]
let authentication =
  let uri = "http://example.com/resource/4?a=1&b=2"

  let bewit_request f_inspect =
    let uri_builder = UriBuilder uri
    let opts =
      { BewitOptions.credentials = creds_inner
        ttl                      = Duration.FromSeconds 300L
        clock                    = clock
        local_clock_offset       = ts 1356420407232L - clock.Now
        ext                      = Some "some-app-data" }
      |> f_inspect
    let bewit = Bewit.generate_str_base64 uri opts
    uri_builder.Query <- String.Join("&", [| uri_builder.Query; "bewit=" + bewit |])
    { ``method`` = GET
      uri        = uri_builder.Uri
      host       = None
      port       = None }

  testList "authentication" [
    testCase "it should generate a bewit then succesfully authenticate it" <| fun _ ->
      Server.authenticate_bewit settings (bewit_request id)
      |> ensure_value
      |> fun (attrs, _, user) ->
        Assert.Equal("ext value", Some "some-app-data", attrs.ext)
        Assert.Equal("return value", "steve", user)

    testCase "it should generate a bewit then succesfully authenticate it (no ext)" <| fun _ ->
      Server.authenticate_bewit settings (bewit_request (fun x -> { x with ext = None }))
      |> ensure_value
      |> fun (attrs, _, user) ->
        Assert.Equal("return value", "steve", user)

    testCase "should successfully authenticate a request (last param)" <| fun _ ->
      { ``method`` = GET
        uri        = Uri "http://example.com/resource/4?a=1&b=2&bewit=MTIzNDU2XDQ1MTE0ODQ2MjFcMzFjMmNkbUJFd1NJRVZDOVkva1NFb2c3d3YrdEVNWjZ3RXNmOGNHU2FXQT1cc29tZS1hcHAtZGF0YQ"
        host       = None
        port       = None }
      |> Server.authenticate_bewit settings
      |> ensure_value
      |> fun (attrs, _, user) ->
        Assert.Equal("ext value", Some "some-app-data", attrs.ext)
        Assert.Equal("return value", "steve", user)

    testCase "should successfully authenticate a request (first param)" <| fun _ ->
      { ``method`` = GET
        uri        = Uri "http://example.com/resource/4?bewit=MTIzNDU2XDQ1MTE0ODQ2MjFcMzFjMmNkbUJFd1NJRVZDOVkva1NFb2c3d3YrdEVNWjZ3RXNmOGNHU2FXQT1cc29tZS1hcHAtZGF0YQ&a=1&b=2"
        host       = None
        port       = None }
      |> Server.authenticate_bewit settings
      |> ensure_value
      |> fun (attrs, _, user) ->
        Assert.Equal("return value", Some "some-app-data", attrs.ext)
        Assert.Equal("return value", "steve", user)

    testCase "should successfully authenticate a request (only param)" <| fun _ ->
      { ``method`` = GET
        uri        = Uri "http://example.com/resource/4?bewit=MTIzNDU2XDQ1MTE0ODQ2MjFcMzFjMmNkbUJFd1NJRVZDOVkva1NFb2c3d3YrdEVNWjZ3RXNmOGNHU2FXQT1cc29tZS1hcHAtZGF0YQ"
        host       = None
        port       = None }
      |> Server.authenticate_bewit settings
      |> ensure_value
      |> fun (attrs, _, user) ->
        Assert.Equal("return value", Some "some-app-data", attrs.ext)
        Assert.Equal("return value", "steve", user) 
  ]