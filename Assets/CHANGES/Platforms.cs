using UnityEngine;
using UnityEngine.Tilemaps;
using Platformer.Mechanics;

public class PlataformasCambiantes : MonoBehaviour
{
    public GameObject grupoAzul;
    public GameObject grupoNaranja;

    public PlayerController jugador;

    [Range(0f, 1f)]
    public float opacidadInactiva = 0.25f;

    private bool azulActivo = true;

    void Start()
    {
        ActualizarPlataformas();
    }

    void Update()
    {
        if (!Input.GetMouseButtonDown(1) || !jugador.controlEnabled)
            return;

        // Probar el nuevo circuito.
        azulActivo = !azulActivo;
        ActualizarPlataformas();

        Physics2D.SyncTransforms();

        GameObject grupoNuevo = azulActivo ? grupoAzul : grupoNaranja;

        foreach (Collider2D col in grupoNuevo.GetComponentsInChildren<Collider2D>())
        {
            if (!col.enabled || col.isTrigger)
                continue;

            ColliderDistance2D contacto = col.Distance(jugador.collider2d);

            if (contacto.isValid && contacto.isOverlapped)
            {
                // El jugador quedaría dentro: restaurar el circuito anterior.
                azulActivo = !azulActivo;
                ActualizarPlataformas();

                Physics2D.SyncTransforms();
                break;
            }
        }
    }

    void ActualizarPlataformas()
    {
        AplicarEstado(grupoAzul, azulActivo);
        AplicarEstado(grupoNaranja, !azulActivo);
    }

    void AplicarEstado(GameObject grupo, bool activo)
    {
       
        foreach (Collider2D col in grupo.GetComponentsInChildren<Collider2D>())
        {
            col.enabled = activo;
        }

        float opacidad = activo ? 1f : opacidadInactiva;

        
        foreach (SpriteRenderer sprite in grupo.GetComponentsInChildren<SpriteRenderer>())
        {
            Color color = sprite.color;
            color.a = opacidad;
            sprite.color = color;
        }

       
        foreach (Tilemap mapa in grupo.GetComponentsInChildren<Tilemap>())
        {
            Color color = mapa.color;
            color.a = opacidad;
            mapa.color = color;
        }
    }
}