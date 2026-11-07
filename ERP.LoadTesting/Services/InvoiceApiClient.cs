using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ERP.LoadTesting.Models;

namespace ERP.LoadTesting.Services
{
    public class InvoiceApiClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _endpoint;
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public InvoiceApiClient(string baseUrl, string endpoint)
        {
            _endpoint = endpoint;
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };
            // Set typical ERP headers
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        public async Task<(bool IsSuccess, int StatusCode, long ResponseBytes, string? ErrorMessage)> SendInvoiceAsync(InvoiceHeaderPayload payload)
        {
            try
            {
                string json = JsonSerializer.Serialize(payload, _jsonOptions);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var response = await _httpClient.PostAsync(_endpoint, content);
                byte[] responseBytes = await response.Content.ReadAsByteArrayAsync();

                if (!response.IsSuccessStatusCode)
                {
                    string errorText = Encoding.UTF8.GetString(responseBytes);
                    return (false, (int)response.StatusCode, responseBytes.Length, $"HTTP {(int)response.StatusCode}: {errorText}");
                }

                return (true, (int)response.StatusCode, responseBytes.Length, null);
            }
            catch (TaskCanceledException)
            {
                return (false, 408, 0, "Request timed out after 30 seconds.");
            }
            catch (Exception ex)
            {
                return (false, 500, 0, ex.Message);
            }
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
