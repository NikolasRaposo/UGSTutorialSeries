using UnityEngine;

namespace Core.Debugging
{
    /// <summary>
    /// Classe base que adiciona funcionalidades de log controlado e formatado.
    /// Substitua 'MonoBehaviour' por esta classe nos seus scripts.
    /// </summary>
    public class DebuggableMonoBehaviour : MonoBehaviour
    {
        [Header("Debug Settings")]
        [SerializeField, Tooltip("Ativa ou desativa os logs deste script específico.")]
        private bool _showLogs = true;

        [SerializeField, Tooltip("Cor do prefixo (Nome da Classe) no console.")]
        private Color _logColor = Color.cyan;

        // Cache do nome formatado para não gerar lixo de memória (Garbage Collection) a cada frame
        private string _formattedPrefix;

        protected virtual void Awake()
        {
            // Prepara o prefixo uma única vez: <color=HEX>[NomeDaClasse]</color>
            string hexColor = ColorUtility.ToHtmlStringRGB(_logColor);
            _formattedPrefix = $"<color=#{hexColor}><b>[{GetType().Name}]</b></color>";
        }

        /// <summary>
        /// Log de informação padrão. Só aparece se _showLogs for true.
        /// </summary>
        protected void Log(object message)
        {
            if (!_showLogs) return;
            Debug.Log($"{_formattedPrefix} {message}", this);
        }

        /// <summary>
        /// Log de aviso (Warning). Sempre aparece ou opcionalmente controlado.
        /// </summary>
        protected void LogWarning(object message)
        {
            // Warnings geralmente queremos ver mesmo com logs desligados, 
            // mas você pode colocar o 'if (!_showLogs) return;' aqui se preferir.
            Debug.LogWarning($"{_formattedPrefix} {message}", this);
        }

        /// <summary>
        /// Log de erro. Erros críticos devem sempre aparecer.
        /// </summary>
        protected void LogError(object message)
        {
            Debug.LogError($"{_formattedPrefix} {message}", this);
        }
    }
}
