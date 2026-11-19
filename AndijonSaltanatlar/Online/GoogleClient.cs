namespace AndijonSaltanatlar.Online
{
    /// <summary>
    /// Google OAuth "Web application" Client ID (ochiq identifikator, maxfiy emas — server ham /api/auth/google/config da beradi).
    /// Serversiz rejimda telefon Google akkaunt oynasi shu ID bilan ochiladi. Client SECRET bu yerda yo'q va bo'lmasligi kerak.
    /// </summary>
    public static class GoogleClient
    {
        public const string WebClientId = "134232366886-4c4efcvfc7qj9da1mq77updh730e58fh.apps.googleusercontent.com";
    }
}
