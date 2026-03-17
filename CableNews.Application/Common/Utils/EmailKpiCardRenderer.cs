namespace CableNews.Application.Common.Utils;

using CableNews.Application.Common.Models;
using System.Linq;

public static class EmailKpiCardRenderer
{
    public static string RenderKpiBar(PrMetricsReport metrics, string brandColor)
    {
        var sovWidth = (int)Math.Clamp(metrics.ShareOfVoice, 0, 100);
        var compWidth = (int)Math.Clamp(metrics.CompetitorShareOfVoice, 0, 100);
        var sentimentColor = metrics.AverageSentiment switch
        {
            >= 70 => "#27ae60",
            >= 40 => "#f39c12",
            _ => "#e74c3c"
        };
        
        var topComp = string.IsNullOrEmpty(metrics.TopCompetitor) || metrics.TopCompetitor == "Ninguno" ? "Principal Competidor" : metrics.TopCompetitor;

        return $$"""
        <table cellpadding="0" cellspacing="0" width="100%" style="margin:0 0 25px 0; font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;">
          <tr>
            <td style="padding:15px; background:#f8f9fa; border-left:4px solid {{brandColor}};">
              <table cellpadding="8" cellspacing="0" width="100%">
                <tr>
                  <td width="25%" align="center" style="border-right:1px solid #e0e0e0; vertical-align: top;">
                    <p style="margin:0;font-size:28px;font-weight:bold;color:{{brandColor}};">{{sovWidth}}%</p>
                    <p style="margin:4px 0 0;font-size:11px;color:#666;text-transform:uppercase;">Share of Voice</p>
                  </td>
                  <td width="25%" align="center" style="border-right:1px solid #e0e0e0; vertical-align: top;">
                    <p style="margin:0;font-size:28px;font-weight:bold;color:{{sentimentColor}};">{{(int)metrics.AverageSentiment}}</p>
                    <p style="margin:4px 0 0;font-size:11px;color:#666;text-transform:uppercase;">Sentimiento (0-100)</p>
                  </td>
                  <td width="25%" align="center" style="border-right:1px solid #e0e0e0; vertical-align: top;">
                    <p style="margin:0;font-size:28px;font-weight:bold;color:#1a1a2e;">{{metrics.TotalArticles}}</p>
                    <p style="margin:4px 0 0;font-size:11px;color:#666;text-transform:uppercase;">Noticias en Radar</p>
                  </td>
                  <td width="25%" align="center" style="vertical-align: top;">
                    <p style="margin:0;font-size:28px;font-weight:bold;color:#1a1a2e;">{{metrics.RelevantArticles}}</p>
                    <p style="margin:4px 0 0;font-size:11px;color:#666;text-transform:uppercase;">Selección Ejecutiva</p>
                  </td>
                </tr>
              </table>
              <p style="margin:10px 0 0; font-size: 11px; color: #666; text-align: center;">Menciones de marca detectadas: <strong>{{metrics.NexansMentions}}</strong></p>
              <table cellpadding="0" cellspacing="0" width="100%" style="margin-top:20px;">
                <tr>
                  <td width="{{sovWidth}}%" style="background:{{brandColor}};height:8px;border-radius:4px 0 0 4px; line-height: 8px; font-size: 8px;">&nbsp;</td>
                  <td width="{{compWidth}}%" style="background:#e74c3c;height:8px; line-height: 8px; font-size: 8px;">&nbsp;</td>
                  <td style="background:#e0e0e0;height:8px;border-radius:0 4px 4px 0; line-height: 8px; font-size: 8px;">&nbsp;</td>
                </tr>
              </table>
              <p style="margin:8px 0 0;font-size:11px;color:#666;text-align:center;">
                <span style="color:{{brandColor}};">■</span> <strong>Nexans</strong> &nbsp;&nbsp;
                <span style="color:#e74c3c;">■</span> <strong>{{topComp}}</strong> &nbsp;&nbsp;
                <span style="color:#e0e0e0;">■</span> Industria
              </p>
              {{(metrics.CrisisDetected ? $$"""
            <div style='background-color: #FEF2F2; border: 2px solid #DC2626; color: #991B1B; padding: 15px; border-radius: 8px; margin-bottom: 20px;'>
                <strong style='display: block; font-size: 16px;'>🚨 ALERTA DE CRISIS DETECTADA</strong>
                <p style='margin: 5px 0 0 0; font-size: 14px;'>Se ha detectado un volumen inusual de noticias negativas o críticas para la marca ({{metrics.NexansMentions}} menciones, {{metrics.CompetitorMentions.Values.Sum()}} de competencia). Por favor, revise las noticias marcadas en <span style='color: #B91C1C; font-weight: bold;'>rojo</span> en el resumen técnico.</p>
            </div>
            """ : "")}}
            </td>
          </tr>
        </table>
        """;
    }

    public static string RenderGlossary()
    {
        return """
        <table cellpadding="0" cellspacing="0" width="100%" style="margin:25px 0 0 0; font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;">
          <tr>
            <td style="padding:15px; background:#f8f9fa; border-left:4px solid #007bff;">
              <h3 style="margin:0 0 10px 0; color:#333; font-size:16px;">Glosario de Métricas</h3>
              <ul style="margin:0; padding:0; list-style:none; font-size:12px; color:#555;">
                <li style="margin-bottom:5px;"><strong>Noticias en Radar:</strong> Total de artículos escaneados inicialmente por el sistema antes del filtrado por relevancia.</li>
                <li style="margin-bottom:5px;"><strong>Selección Ejecutiva:</strong> Número de noticias finales que superaron el filtro de calidad y relevancia técnica para aparecer en este reporte.</li>
                <li style="margin-bottom:5px;"><strong>Menciones de Marca:</strong> Conteo total de veces que Nexans o sus filiales aparecen en las noticias analizadas. El sistema prioriza mostrar todos estos eventos en el resumen.</li>
                <li style="margin-bottom:5px;"><strong>Share of Voice (SoV):</strong> Porcentaje de presencia de nuestra marca frente a los competidores mencionados.</li>
                <li style="margin-bottom:5px;"><strong>Sentimiento (0-100):</strong> Salud de la marca. >70 Positivo, 40-69 Neutral, <40 Negativo.</li>
                <li style="margin-bottom:5px;"><strong>Menciones Competidores:</strong> Número de veces que los competidores fueron mencionados en las noticias.</li>
                <li style="margin-bottom:5px;"><strong>Top Competidor:</strong> El competidor con el mayor número de menciones en el periodo.</li>
                <li style="margin-bottom:5px;"><strong>Crisis Detectada:</strong> Alerta generada cuando se detecta un volumen inusual de noticias negativas o críticas para la marca.</li>
              </ul>
            </td>
          </tr>
        </table>
        """;
    }
}
