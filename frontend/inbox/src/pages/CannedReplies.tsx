import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { api, del, post } from "../api";
import type { CannedReplyDto, TeamDto } from "../types";

const schema = z.object({
  shortcut: z.string().min(1).regex(/^[a-zA-Z0-9_-]+$/, "Letters, digits, - and _ only"),
  title: z.string().min(1),
  body: z.string().min(1),
  personal: z.boolean(),
  teamId: z.string().optional(),
});
type FormValues = z.infer<typeof schema>;

export default function CannedRepliesPage() {
  const queryClient = useQueryClient();
  const replies = useQuery({
    queryKey: ["canned-replies"],
    queryFn: () => api<CannedReplyDto[]>("/canned-replies"),
  });
  const teams = useQuery({ queryKey: ["teams"], queryFn: () => api<TeamDto[]>("/teams") });

  const { register, handleSubmit, reset, watch, formState } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { personal: true },
  });

  const save = useMutation({
    mutationFn: (values: FormValues) =>
      post("/canned-replies", { ...values, teamId: values.personal ? null : values.teamId }),
    onSuccess: () => {
      reset({ shortcut: "", title: "", body: "", personal: true });
      queryClient.invalidateQueries({ queryKey: ["canned-replies"] });
    },
  });

  const remove = useMutation({
    mutationFn: (id: string) => del(`/canned-replies/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["canned-replies"] }),
  });

  if (replies.isLoading) return <div className="page-loading">Loading…</div>;
  if (replies.isError) return <div className="error page-loading">Failed to load canned replies.</div>;

  return (
    <main className="canned-page">
      <section>
        <h2>Saved replies</h2>
        <p className="muted">
          Insert in the composer with /shortcut. Placeholders: {"{{visitor.name}}"}, {"{{agent.name}}"},{" "}
          {"{{team.name}}"}.
        </p>
        {replies.data!.length === 0 && <p className="muted">No replies yet — create the first one.</p>}
        <ul className="canned-list">
          {replies.data!.map((r) => (
            <li key={r.id} className="card">
              <div className="canned-head">
                <code>/{r.shortcut}</code>
                <strong>{r.title}</strong>
                <span className="muted">{r.agentUserId ? "personal" : "team"}</span>
                <button className="link" onClick={() => remove.mutate(r.id)}>
                  Delete
                </button>
              </div>
              <p>{r.body}</p>
            </li>
          ))}
        </ul>
      </section>

      <section>
        <h2>New reply</h2>
        <form className="card form" onSubmit={handleSubmit((v) => save.mutate(v))}>
          <label>
            Shortcut
            <input {...register("shortcut")} placeholder="hello" />
            {formState.errors.shortcut && <span className="error">{formState.errors.shortcut.message}</span>}
          </label>
          <label>
            Title
            <input {...register("title")} />
          </label>
          <label>
            Body
            <textarea rows={4} {...register("body")} placeholder="Hi {{visitor.name}}, …" />
          </label>
          <label className="row">
            <input type="checkbox" {...register("personal")} /> Personal (only me)
          </label>
          {!watch("personal") && (
            <label>
              Team
              <select {...register("teamId")}>
                {teams.data?.map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.name}
                  </option>
                ))}
              </select>
            </label>
          )}
          {save.isError && <div className="error">{(save.error as Error).message}</div>}
          <button type="submit" disabled={save.isPending}>
            Save
          </button>
        </form>
      </section>
    </main>
  );
}
