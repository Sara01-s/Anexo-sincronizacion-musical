# Anexo-sincronizacion-musical
Recursos, bibliografía y notas importantes

## Cómo sincronizar tu juego con la música
Sara San Martín, septiembre 2025.

Referentes:
- Exceed7: https://exceed7.com/native-audio/rhythm-game-crash-course/backing-track.html
(Exceed7 es el creador de Native Audio Plugin para Unity, una solución que introduce mejoras a la API de Audio de Unity para minimizar la latencia en dispositivos móviles, su blog explica a gran detalle como funciona el Sistema de Audio interno del motor).
- DDRKirby's work: https://rhythmquestgame.com/devlog/04.html
(DDRKirby es creador de Rhythm Quest, un videojuego que a fecha actual está por publicarse, en su blog explica las diferentes complicaciones de ajustar la latencia y como usar una regresión lineal).
- Freya Holmér: https://x.com/FreyaHolmer/status/845954862403780609
(Freya planteó el problema del Tiempo DSP ruidoso en un post de 2017, también planteó el uso de regresión lineal para solucionarlo).
- Peer Play: https://www.youtube.com/@PeerPlay
(Peer Play tiene excelentes videos sobre como trabajar el audio en Unity, sacando el máximo provecho al buffer circular de audio).

### Cómo usar Smooth DSP.

Los ejemplos de código C# adjuntos al repositorio muestras detalles interesantes sobre el uso de esta técnica para obtener un tiempo dsp suavizado.

Es importante notar como dice Exceed7 y DDRKirby (ver referentes) que no se debe usar AudioSource.Play(), si no AudioSource.PlayScheduled(clip, dspTime), y siempre a futuro, nunca en el mismo frame, enfatizo en investigar sobre esto antes de utilizar smoothdsp.

Las solución a GetEstimatedLatency está en AudioEngine.cs (ver código adjunto).

### Extra.
Gráfico de DSP time en Godot.

https://github.com/user-attachments/assets/3761cf9e-9332-43a7-b270-aab8f2d70e8b







